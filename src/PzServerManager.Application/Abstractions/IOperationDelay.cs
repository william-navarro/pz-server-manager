namespace PzServerManager.Application.Abstractions;

public interface IOperationDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}
