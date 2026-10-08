using PzServerManager.Domain.Servers;

namespace PzServerManager.Application.Abstractions;

public interface IServerInstanceRepository
{
    Task<IReadOnlyList<ServerInstance>> ListAsync(CancellationToken cancellationToken = default);
    Task<ServerInstance?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ServerInstance instance, CancellationToken cancellationToken = default);
    Task UpdateAsync(ServerInstance instance, CancellationToken cancellationToken = default);
}
