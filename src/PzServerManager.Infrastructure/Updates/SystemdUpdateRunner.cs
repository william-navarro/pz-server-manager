using PzServerManager.Application.Abstractions;

namespace PzServerManager.Infrastructure.Updates;

public sealed class SystemdUpdateRunner(
    ICommandRunner commands,
    IServerOperationLock operationLock,
    IOperationDelay delay) : ISystemUpdateRunner
{
    public const string UnitName = "pz-atualizar.service";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    public bool IsRunning => operationLock.CurrentOperation == "atualização do jogo";

    public async Task<SystemUpdateResult> RunAsync(CancellationToken cancellationToken = default)
    {
        using var lease = operationLock.TryAcquire("atualização do jogo");
        if (lease is null) throw new InvalidOperationException($"Outra operação já está em andamento: {operationLock.CurrentOperation}.");
        var start = await commands.RunAsync("systemctl", ["start", "--no-block", UnitName], CommandTimeout, cancellationToken);
        if (!start.IsSuccess) return new(false, Detail(start), string.Empty);
        for (var poll = 0; poll < 600; poll++)
        {
            await delay.DelayAsync(TimeSpan.FromSeconds(2), cancellationToken);
            var status = await commands.RunAsync("systemctl", ["show", UnitName, "--property=ActiveState,Result"], CommandTimeout, cancellationToken);
            if (!status.IsSuccess) return new(false, Detail(status), await JournalAsync(cancellationToken));
            var state = ManagedServer.SystemdBackupParser.ParseOperationState(status.StandardOutput);
            if (state.ActiveState is "active" or "activating" or "deactivating" or "reloading") continue;
            var succeeded = state.ActiveState == "inactive" && state.Result.Equals("success", StringComparison.OrdinalIgnoreCase);
            return new(succeeded, succeeded ? "Jogo atualizado e servidor iniciado." : $"A atualização falhou ({state.Result}).", await JournalAsync(cancellationToken));
        }
        return new(false, "A atualização excedeu 20 minutos.", await JournalAsync(cancellationToken));
    }

    private async Task<string> JournalAsync(CancellationToken cancellationToken)
    {
        var result = await commands.RunAsync("journalctl", ["-u", UnitName, "-n", "50", "--no-pager", "--output=short-iso"], CommandTimeout, cancellationToken);
        return result.IsSuccess ? result.StandardOutput.Trim() : Detail(result);
    }
    private static string Detail(CommandResult result) => string.IsNullOrWhiteSpace(result.StandardError) ? $"código {result.ExitCode}" : result.StandardError.Trim();
}
