using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.ManagedServer;

public sealed class SystemdServerController : IServerController
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(2);
    private readonly ICommandRunner _commandRunner;
    private readonly ManagedServerOptions _options;
    private readonly IAuditLog _auditLog;
    private readonly IServerOperationLock _operationLock;

    public SystemdServerController(
        ICommandRunner commandRunner,
        IOptions<ManagerOptions> options,
        IAuditLog auditLog,
        IServerOperationLock operationLock)
    {
        _commandRunner = commandRunner;
        _options = options.Value.ManagedServer
            ?? throw new InvalidOperationException("O servidor Linux gerenciado não foi configurado.");
        _auditLog = auditLog;
        _operationLock = operationLock;
    }

    public async Task<ManagedServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            "systemctl",
            ["show", _options.ServiceName, "--property=ActiveState,SubState,MainPID,ActiveEnterTimestamp,NRestarts,MemoryCurrent,CPUUsageNSec"],
            StatusTimeout,
            cancellationToken);

        if (!result.IsSuccess)
        {
            throw CreateCommandException("consultar o status", result);
        }

        var data = SystemdShowParser.Parse(result.StandardOutput);
        var systemMemory = await TryReadSystemMemoryAsync(cancellationToken);
        return new ManagedServerStatus(
            MapState(data.ActiveState, data.SubState),
            data.ActiveState,
            data.SubState,
            data.MainProcessId,
            data.ActiveEnterTimestamp,
            data.RestartCount,
            data.MemoryCurrent,
            systemMemory.AvailableBytes,
            systemMemory.TotalBytes,
            data.CpuUsageNanoseconds);
    }

    public Task StartAsync(string userName, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync("start", "iniciar", userName, StartTimeout, cancellationToken);

    public Task StopAsync(string userName, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync("stop", "parar", userName, StopTimeout, cancellationToken);

    public Task RestartAsync(string userName, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync("restart", "reiniciar", userName, StopTimeout, cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        StartAsync("sistema", cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        StopAsync("sistema", cancellationToken);

    public Task RestartAsync(CancellationToken cancellationToken = default) =>
        RestartAsync("sistema", cancellationToken);

    private async Task ExecuteActionAsync(
        string systemdAction,
        string auditAction,
        string userName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        using var lease = _operationLock.TryAcquire($"{auditAction} servidor");
        if (lease is null)
        {
            throw new InvalidOperationException($"Outra operação já está em andamento: {_operationLock.CurrentOperation ?? "ação do servidor"}.");
        }

        try
        {
            var result = await _commandRunner.RunAsync(
                "systemctl",
                [systemdAction, _options.ServiceName],
                timeout,
                cancellationToken);
            if (!result.IsSuccess)
            {
                var exception = CreateCommandException(auditAction, result);
                await _auditLog.WriteAsync(userName, auditAction, $"erro: {exception.Message}", CancellationToken.None);
                throw exception;
            }

            await _auditLog.WriteAsync(userName, auditAction, "sucesso", CancellationToken.None);
        }
        catch (Exception exception) when (exception is not InvalidOperationException || !exception.Message.StartsWith("Falha ao", StringComparison.Ordinal))
        {
            await _auditLog.WriteAsync(userName, auditAction, $"erro: {exception.Message}", CancellationToken.None);
            throw;
        }
    }

    private static ManagedServerState MapState(string activeState, string subState) =>
        (activeState, subState) switch
        {
            ("active", "running") => ManagedServerState.Running,
            ("activating", _) => ManagedServerState.Starting,
            ("deactivating", _) => ManagedServerState.Stopping,
            ("failed", _) => ManagedServerState.Failed,
            ("inactive", _) => ManagedServerState.Stopped,
            _ => ManagedServerState.Unknown
        };

    private static async Task<SystemMemoryInfo> TryReadSystemMemoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync("/proc/meminfo", cancellationToken);
            return ProcFileParser.ParseSystemMemory(text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SystemMemoryInfo(null, null);
        }
    }

    private static InvalidOperationException CreateCommandException(string operation, CommandResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? $"código {result.ExitCode}"
            : result.StandardError.Trim();
        return new InvalidOperationException($"Falha ao {operation} o serviço: {detail}");
    }
}
