using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Automation;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Application.Mods;
using PzServerManager.Application.Updates;
using PzServerManager.Infrastructure.Configuration;
using PzServerManager.Infrastructure.Steam;

namespace PzServerManager.Infrastructure.Updates;

public sealed class UpdateManager : IUpdateManager
{
    private sealed record State(UpdateOverview Overview);
    private readonly IServerIniStore _iniStore;
    private readonly IWorkshopMetadataClient _metadata;
    private readonly ICommandRunner _commands;
    private readonly IManagerPaths _paths;
    private readonly IBackupManager _backups;
    private readonly ISystemUpdateRunner _systemUpdate;
    private readonly ServerRestartCoordinator _restart;
    private readonly IMetricsStore _metrics;
    private readonly IAuditLog _audit;
    private readonly IAutomationSettingsStore _settings;
    private readonly TimeProvider _time;
    private readonly ManagedServerOptions _options;
    private readonly string _statePath;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    public UpdateManager(IServerIniStore iniStore, IWorkshopMetadataClient metadata, ICommandRunner commands,
        IManagerPaths paths, IBackupManager backups, ISystemUpdateRunner systemUpdate,
        ServerRestartCoordinator restart, IMetricsStore metrics, IAuditLog audit,
        IAutomationSettingsStore settings, TimeProvider time, IOptions<ManagerOptions> options)
    {
        _iniStore = iniStore; _metadata = metadata; _commands = commands; _paths = paths;
        _backups = backups; _systemUpdate = systemUpdate; _restart = restart; _metrics = metrics;
        _audit = audit; _settings = settings; _time = time;
        _options = options.Value.ManagedServer ?? throw new InvalidOperationException("O servidor Linux gerenciado não foi configurado.");
        _statePath = Path.Combine(paths.DataRoot, "update-history.json");
    }

    public bool IsBusy => _operation.CurrentCount == 0 || _restart.IsScheduled || _systemUpdate.IsRunning;

    public async Task<UpdateOverview> GetOverviewAsync(CancellationToken cancellationToken = default) =>
        (await ReadStateAsync(cancellationToken)).Overview;

    public async Task<UpdateOverview> CheckNowAsync(string actor, CancellationToken cancellationToken = default)
    {
        if (!await _operation.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("Já existe uma verificação ou atualização em andamento.");
        try
        {
            var previous = (await ReadStateAsync(cancellationToken)).Overview;
            try
            {
                var ini = await _iniStore.LoadAsync(_options.ServerIniPath, cancellationToken);
                var ids = ini.GetList("WorkshopItems");
                var published = await _metadata.GetAsync(ids, cancellationToken);
                var workshopPath = Path.Combine(_options.InstallPath, "steamapps", "workshop", "appworkshop_108600.acf");
                var installed = File.Exists(workshopPath)
                    ? SteamVdfParser.ParseWorkshopUpdates(await File.ReadAllTextAsync(workshopPath, cancellationToken))
                    : new Dictionary<string, DateTimeOffset?>();
                var outdated = ids.Where(published.ContainsKey).Select(id => (id, item: published[id]))
                    .Where(value => value.item.UpdatedAt is not null &&
                        (!installed.TryGetValue(value.id, out var installedAt) || installedAt is null || value.item.UpdatedAt > installedAt))
                    .Select(value => new OutdatedWorkshopItem(value.id, value.item.Title,
                        installed.GetValueOrDefault(value.id), value.item.UpdatedAt!.Value)).ToArray();

                var manifestPath = Path.Combine(_options.InstallPath, "steamapps", "appmanifest_380870.acf");
                var installedBuild = File.Exists(manifestPath)
                    ? SteamVdfParser.ParseInstalledBuild(await File.ReadAllTextAsync(manifestPath, cancellationToken)) : null;
                // A falha ao consultar o build do jogo não pode impedir a atualização dos mods: registra o erro com o
                // prefixo GameCheckErrorPrefix e deixa o jogo como "desconhecido" (nunca atualiza o jogo às cegas).
                string? publishedBuild = null;
                string? gameError = null;
                try
                {
                    var appInfo = await _commands.RunAsync(Path.Combine(_paths.SteamCmdRoot, "steamcmd.sh"),
                        ["+login", "anonymous", "+app_info_update", "1", "+app_info_print", "380870", "+quit"],
                        TimeSpan.FromMinutes(2), cancellationToken);
                    if (appInfo.IsSuccess) publishedBuild = SteamVdfParser.ParsePublicBuild(appInfo.StandardOutput);
                    if (publishedBuild is null)
                        gameError = string.IsNullOrWhiteSpace(appInfo.StandardError) ? "SteamCMD não retornou o build publicado." : appInfo.StandardError.Trim();
                }
                catch (Exception exception) when (exception is not OperationCanceledException) { gameError = exception.Message; }
                var overview = previous with { LastCheckedAt = _time.GetUtcNow(), OutdatedMods = outdated,
                    InstalledGameBuild = installedBuild, PublishedGameBuild = publishedBuild,
                    LastError = gameError is null ? null : UpdateOverview.GameCheckErrorPrefix + gameError };
                await WriteStateAsync(new(overview), cancellationToken);
                await _audit.WriteAsync(actor, "verificar atualizações", $"{outdated.Length} mod(s) desatualizado(s)", CancellationToken.None);
                return overview;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var overview = previous with { LastCheckedAt = _time.GetUtcNow(), LastError = exception.Message };
                await WriteStateAsync(new(overview), CancellationToken.None);
                await _audit.WriteAsync(actor, "verificar atualizações", $"erro: {exception.Message}", CancellationToken.None);
                return overview;
            }
        }
        finally { _operation.Release(); }
    }

    public async Task ApplyAsync(string actor, UpdateTarget target, CancellationToken cancellationToken = default)
    {
        if (!await _operation.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("Já existe uma verificação ou atualização em andamento.");
        try
        {
            var overview = (await ReadStateAsync(cancellationToken)).Overview;
            var applyMods = (target is UpdateTarget.Mods or UpdateTarget.All) && overview.OutdatedMods.Count > 0;
            var applyGame = (target is UpdateTarget.Game or UpdateTarget.All) && overview.IsGameOutdated;
            if (!applyMods && !applyGame) throw new InvalidOperationException("Não há atualização detectada para o item escolhido.");
            var description = applyGame && applyMods ? "O jogo e mods foram atualizados" : applyGame ? "O jogo foi atualizado" :
                $"O mod {overview.OutdatedMods[0].Title} foi atualizado";
            string? generatedBackup = null;
            try
            {
                await RunWithCountdownAsync(actor, description, async token =>
                {
                    if (applyGame)
                    {
                        var result = await _systemUpdate.RunAsync(token);
                        generatedBackup = ExtractBackupName(result.Journal);
                        if (!result.Succeeded) throw new InvalidOperationException(result.Message);
                    }
                    else
                    {
                        var backup = await _backups.CreateBackupAsync(actor, "atualizacao-mods", token);
                        generatedBackup = ExtractBackupName(backup.Journal);
                        if (!backup.Succeeded) throw new InvalidOperationException($"O backup falhou; o servidor não foi reiniciado. {backup.Message}");
                        await _restart.RestartNowAsync(actor, token);
                    }
                    await RecordResultAsync(target, description, generatedBackup, true, "Concluída", token);
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await RecordResultAsync(target, description, generatedBackup, false, "Cancelada", CancellationToken.None);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RecordResultAsync(target, description, generatedBackup, false, exception.Message, CancellationToken.None);
                throw;
            }
        }
        finally { _operation.Release(); }
    }

    private async Task RunWithCountdownAsync(string actor, string description, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? players = null;
        try { players = await _restart.GetPlayersAsync(cancellationToken); } catch { }
        if (players is { Count: 0 }) await action(cancellationToken);
        else await _restart.WarnAndExecuteAsync(actor, description, action, cancellationToken);
    }

    private static string? ExtractBackupName(string journal)
    {
        const string marker = "backup criado: ";
        var position = journal.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (position < 0) return null;
        var value = journal[(position + marker.Length)..].Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return value is null ? null : Path.GetFileName(value);
    }

    private async Task RecordResultAsync(UpdateTarget target, string description, string? backup, bool succeeded, string result, CancellationToken token)
    {
        var state = await ReadStateAsync(token);
        var history = state.Overview.History.Prepend(new UpdateHistoryEntry(_time.GetUtcNow(), target, description, backup, succeeded, result)).Take(100).ToArray();
        var overview = state.Overview with { History = history };
        if (succeeded)
        {
            if (target is UpdateTarget.Mods or UpdateTarget.All) overview = overview with { OutdatedMods = [] };
            if (target is UpdateTarget.Game or UpdateTarget.All) overview = overview with { InstalledGameBuild = overview.PublishedGameBuild };
        }
        await WriteStateAsync(new(overview), token);
        if (succeeded) await _metrics.AppendEventAsync(new(_time.GetUtcNow(), "atualizacao", description), token);
    }

    private async Task<State> ReadStateAsync(CancellationToken token)
    {
        await _stateGate.WaitAsync(token);
        try { return await AtomicJsonFile.ReadAsync<State>(_statePath, token) ?? new(new(null, [], null, null, [], null)); }
        finally { _stateGate.Release(); }
    }
    private async Task WriteStateAsync(State state, CancellationToken token)
    {
        await _stateGate.WaitAsync(token);
        try { await AtomicJsonFile.WriteAsync(_statePath, state, token); }
        finally { _stateGate.Release(); }
    }
}
