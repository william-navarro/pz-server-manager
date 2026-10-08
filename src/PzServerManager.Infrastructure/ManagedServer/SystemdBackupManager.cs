using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Backups;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.ManagedServer;

public sealed class SystemdBackupManager(
    ICommandRunner commandRunner,
    IOptions<ManagerOptions> options,
    IAuditLog auditLog,
    IServerOperationLock operationLock,
    IOperationDelay delay,
    BackupFileNameParser backupFileNameParser) : IBackupManager
{
    public const string ManualUnit = "pz-backup.service";
    public const string DailyUnit = "pz-backup-diario.service";
    public const string DailyTimer = "pz-backup-diario.timer";
    public const string ResetUnit = "pz-zerar-mundo.service";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int MaximumPolls = 300;
    private readonly ManagedServerOptions _options = options.Value.ManagedServer
        ?? throw new InvalidOperationException("O servidor Linux gerenciado não foi configurado.");

    public bool IsOperationInProgress => operationLock.IsBusy;
    public string? CurrentOperation => operationLock.CurrentOperation;

    public async Task<BackupOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var files = ReadBackups();
        var timer = await commandRunner.RunAsync(
            "systemctl",
            ["show", DailyTimer, "--property=NextElapseUSecRealtime"],
            CommandTimeout,
            cancellationToken);
        EnsureSuccess("consultar o próximo backup diário", timer);

        var services = await commandRunner.RunAsync(
            "systemctl",
            ["show", ManualUnit, DailyUnit, "--property=Result,ExecMainExitTimestamp"],
            CommandTimeout,
            cancellationToken);
        EnsureSuccess("consultar os últimos backups", services);

        return new BackupOverview(
            files,
            SystemdBackupParser.ParseNextElapse(timer.StandardOutput),
            SystemdBackupParser.ParseServiceResults(services.StandardOutput, [ManualUnit, DailyUnit]));
    }

    public Task<BackupOperationResult> CreateBackupAsync(string actor, CancellationToken cancellationToken = default) =>
        RunOperationAsync(actor, "fazer backup manual", "backup manual", ManualUnit, cancellationToken);

    public Task<BackupOperationResult> CreateBackupAsync(string actor, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 20 || reason.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new ArgumentException("O motivo do backup aceita somente a-z, 0-9 e hífen, com até 20 caracteres.", nameof(reason));
        return RunOperationAsync(actor, $"fazer backup {reason}", $"backup {reason}", $"pz-backup@{reason}.service", cancellationToken);
    }

    public async Task<BackupOperationResult> RestoreAsync(
        string actor,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (!backupFileNameParser.TryParse(fileName, 0, out _) ||
            !ReadBackups().Any(file => file.FileName.Equals(fileName, StringComparison.Ordinal)))
        {
            await auditLog.WriteAsync(actor, "restaurar backup", $"recusado: arquivo inválido ou ausente ({fileName})", CancellationToken.None);
            throw new InvalidOperationException("O backup escolhido não é válido ou não está mais disponível.");
        }

        return await RunOperationAsync(
            actor,
            $"restaurar backup {fileName}",
            "restauração de backup",
            $"pz-restaurar@{fileName}.service",
            cancellationToken);
    }

    public Task<BackupOperationResult> ResetWorldAsync(string actor, CancellationToken cancellationToken = default) =>
        RunOperationAsync(actor, "zerar mundo", "zeragem do mundo", ResetUnit, cancellationToken);

    private async Task<BackupOperationResult> RunOperationAsync(
        string actor,
        string auditAction,
        string operationDescription,
        string unitName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        using var lease = operationLock.TryAcquire(operationDescription);
        if (lease is null)
        {
            await auditLog.WriteAsync(actor, auditAction, "recusado: outra operação em andamento", CancellationToken.None);
            throw new InvalidOperationException($"Outra operação já está em andamento: {operationLock.CurrentOperation ?? "ação do servidor"}.");
        }

        try
        {
            var start = await commandRunner.RunAsync(
                "systemctl",
                ["start", "--no-block", unitName],
                CommandTimeout,
                cancellationToken);
            if (!start.IsSuccess)
            {
                return await FinishAsync(actor, auditAction, unitName, false, false,
                    $"Não foi possível iniciar a operação: {CommandDetail(start)}", cancellationToken);
            }

            for (var poll = 0; poll < MaximumPolls; poll++)
            {
                await delay.DelayAsync(PollInterval, cancellationToken);
                var status = await commandRunner.RunAsync(
                    "systemctl",
                    ["show", unitName, "--property=ActiveState,Result"],
                    CommandTimeout,
                    cancellationToken);
                if (!status.IsSuccess)
                {
                    return await FinishAsync(actor, auditAction, unitName, false, false,
                        $"Não foi possível acompanhar a operação: {CommandDetail(status)}", cancellationToken);
                }

                var state = SystemdBackupParser.ParseOperationState(status.StandardOutput);
                if (state.ActiveState is "activating" or "active" or "reloading" or "deactivating") continue;
                if (state.ActiveState.Equals("inactive", StringComparison.Ordinal) &&
                    state.Result.Equals("success", StringComparison.OrdinalIgnoreCase))
                {
                    return await FinishAsync(actor, auditAction, unitName, true, false, "Operação concluída com sucesso.", cancellationToken);
                }

                if (state.ActiveState.Equals("failed", StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(state.Result) && !state.Result.Equals("success", StringComparison.OrdinalIgnoreCase)))
                {
                    var result = string.IsNullOrWhiteSpace(state.Result) ? state.ActiveState : state.Result;
                    return await FinishAsync(actor, auditAction, unitName, false, false,
                        $"A operação falhou (resultado: {result}).", cancellationToken);
                }
            }

            return await FinishAsync(actor, auditAction, unitName, false, true,
                "A operação excedeu o limite de 10 minutos.", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await auditLog.WriteAsync(actor, auditAction, "cancelado", CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await auditLog.WriteAsync(actor, auditAction, $"erro: {exception.Message}", CancellationToken.None);
            throw;
        }
    }

    private async Task<BackupOperationResult> FinishAsync(
        string actor,
        string auditAction,
        string unitName,
        bool succeeded,
        bool timedOut,
        string message,
        CancellationToken cancellationToken)
    {
        var journal = await ReadJournalAsync(unitName, cancellationToken);
        await auditLog.WriteAsync(
            actor,
            auditAction,
            succeeded ? "sucesso" : timedOut ? "tempo esgotado" : $"erro: {message}",
            CancellationToken.None);
        return new BackupOperationResult(succeeded, timedOut, message, journal);
    }

    private async Task<string> ReadJournalAsync(string unitName, CancellationToken cancellationToken)
    {
        try
        {
            var result = await commandRunner.RunAsync(
                "journalctl",
                ["-u", unitName, "-n", "30", "--no-pager", "--output=short-iso"],
                CommandTimeout,
                cancellationToken);
            return result.IsSuccess ? result.StandardOutput.Trim() : $"Journal indisponível: {CommandDetail(result)}";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return $"Journal indisponível: {exception.Message}";
        }
    }

    private IReadOnlyList<BackupFile> ReadBackups()
    {
        if (!Directory.Exists(_options.BackupsPath)) return Array.Empty<BackupFile>();
        return backupFileNameParser.ParseAndSort(
            Directory.EnumerateFiles(_options.BackupsPath, "*", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Select(file => (file.Name, file.Length)));
    }

    private static void EnsureSuccess(string operation, CommandResult result)
    {
        if (!result.IsSuccess) throw new InvalidOperationException($"Falha ao {operation}: {CommandDetail(result)}");
    }

    private static string CommandDetail(CommandResult result) =>
        string.IsNullOrWhiteSpace(result.StandardError) ? $"código {result.ExitCode}" : result.StandardError.Trim();
}
