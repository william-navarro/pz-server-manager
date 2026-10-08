using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Automation;
using PzServerManager.Application.Rcon;

namespace PzServerManager.Application.ManagedServer;

public sealed record RestartCountdownState(
    bool IsScheduled,
    DateTimeOffset? ExecuteAt,
    int? SecondsRemaining,
    string? Reason,
    string? Actor);

public sealed class ServerRestartCoordinator(
    IServerController controller,
    ServerAdministrationService administration,
    IAuditLog auditLog,
    IAutomationSettingsStore settingsStore,
    IServerOperationLock operationLock,
    TimeProvider timeProvider) : IDisposable
{
    public ServerRestartCoordinator(
        IServerController controller,
        ServerAdministrationService administration,
        IAuditLog auditLog,
        TimeProvider timeProvider)
        : this(controller, administration, auditLog, new DefaultSettingsStore(), new ServerOperationLock(), timeProvider) { }

    public ServerRestartCoordinator(
        IServerController controller,
        ServerAdministrationService administration,
        IAuditLog auditLog,
        IAutomationSettingsStore settingsStore,
        TimeProvider timeProvider)
        : this(controller, administration, auditLog, settingsStore, new ServerOperationLock(), timeProvider) { }

    private readonly object _sync = new();
    private CancellationTokenSource? _scheduledRestart;
    private RestartCountdownState _state = new(false, null, null, null, null);

    public bool IsScheduled { get { lock (_sync) return _scheduledRestart is not null; } }
    public RestartCountdownState State { get { lock (_sync) return _state; } }

    public Task<IReadOnlyList<string>> GetPlayersAsync(CancellationToken cancellationToken = default) =>
        administration.GetPlayersAsync(cancellationToken);

    public Task RestartNowAsync(string actor, CancellationToken cancellationToken = default) =>
        controller.RestartAsync(actor, cancellationToken);

    public Task WarnAndRestartAsync(string actor, CancellationToken cancellationToken = default) =>
        WarnAndExecuteAsync(actor, "Reinício solicitado", token => controller.RestartAsync(actor, token), cancellationToken);

    public async Task WarnAndExecuteAsync(
        string actor,
        string reason,
        Func<CancellationToken, Task> execute,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(execute);
        var settings = (await settingsStore.LoadAsync(cancellationToken)).Validate();
        using var operationLease = operationLock.TryAcquire("contagem regressiva para reinício")
            ?? throw new InvalidOperationException($"Outra operação já está em andamento: {operationLock.CurrentOperation}.");
        CancellationTokenSource source;
        lock (_sync)
        {
            if (_scheduledRestart is not null) throw new InvalidOperationException("Já existe um reinício programado em andamento.");
            source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _scheduledRestart = source;
            var longest = settings.WarningSeconds.Max();
            _state = new(true, timeProvider.GetUtcNow().AddSeconds(longest), longest, reason, actor);
        }

        try
        {
            var previous = settings.WarningSeconds.Max();
            foreach (var seconds in settings.WarningSeconds)
            {
                if (previous > seconds) await Task.Delay(TimeSpan.FromSeconds(previous - seconds), timeProvider, source.Token);
                UpdateRemaining(seconds);
                await administration.SendMessageAsync(actor, BuildWarning(reason, seconds), source.Token);
                previous = seconds;
            }
            await Task.Delay(TimeSpan.FromSeconds(previous), timeProvider, source.Token);
            UpdateRemaining(0);
            operationLease.Dispose();
            await execute(source.Token);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            await auditLog.WriteAsync(actor, "cancelar reinício avisado", "sucesso", CancellationToken.None);
            throw;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_scheduledRestart, source)) _scheduledRestart = null;
                _state = new(false, null, null, null, null);
            }
            source.Dispose();
        }
    }

    public async Task RestartImmediatelyIfEmptyAsync(string actor, string reason, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string>? players = null;
        try { players = await GetPlayersAsync(cancellationToken); } catch { }
        if (players is { Count: 0 }) await controller.RestartAsync(actor, cancellationToken);
        else await WarnAndExecuteAsync(actor, reason, token => controller.RestartAsync(actor, token), cancellationToken);
    }

    public void Cancel() { lock (_sync) _scheduledRestart?.Cancel(); }
    public void Dispose() { lock (_sync) _scheduledRestart?.Cancel(); }

    public static string FormatDuration(int seconds) => seconds switch
    {
        >= 120 when seconds % 60 == 0 => $"{seconds / 60} minutos",
        60 => "1 minuto",
        >= 60 => $"{seconds / 60} min {seconds % 60} s",
        1 => "1 segundo",
        _ => $"{seconds} segundos"
    };

    private static string BuildWarning(string reason, int seconds) =>
        reason == "Reinício solicitado"
            ? $"O servidor vai reiniciar em {FormatDuration(seconds)}"
            : $"{reason}. O servidor vai reiniciar em {FormatDuration(seconds)}.";

    private void UpdateRemaining(int seconds)
    {
        lock (_sync) _state = _state with { SecondsRemaining = seconds };
    }

    private sealed class DefaultSettingsStore : IAutomationSettingsStore
    {
        public Task<AutomationSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AutomationSettings());
        public Task SaveAsync(AutomationSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
