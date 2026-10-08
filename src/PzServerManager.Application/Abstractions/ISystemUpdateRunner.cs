namespace PzServerManager.Application.Abstractions;

public interface ISystemUpdateRunner
{
    bool IsRunning { get; }
    Task<SystemUpdateResult> RunAsync(CancellationToken cancellationToken = default);
}

public sealed record SystemUpdateResult(bool Succeeded, string Message, string Journal);
