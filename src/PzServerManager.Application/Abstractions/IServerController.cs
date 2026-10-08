using PzServerManager.Application.ManagedServer;

namespace PzServerManager.Application.Abstractions;

public interface IServerController
{
    Task<ManagedServerStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task RestartAsync(CancellationToken cancellationToken = default);
    Task StartAsync(string userName, CancellationToken cancellationToken = default);
    Task StopAsync(string userName, CancellationToken cancellationToken = default);
    Task RestartAsync(string userName, CancellationToken cancellationToken = default);
}
