using PzServerManager.Application.Abstractions;

namespace PzServerManager.Infrastructure.ManagedServer;

public sealed class SystemOperationDelay : IOperationDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) =>
        Task.Delay(delay, cancellationToken);
}
