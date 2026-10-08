namespace PzServerManager.Application.Abstractions;

public interface IServerCommandReadiness
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
}
