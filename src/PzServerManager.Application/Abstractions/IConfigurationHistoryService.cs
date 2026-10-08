using PzServerManager.Application.ConfigurationHistory;

namespace PzServerManager.Application.Abstractions;

public interface IConfigurationHistoryService
{
    Task<IReadOnlyList<ConfigurationBackup>> ListAsync(CancellationToken cancellationToken = default);
    Task<ConfigurationComparison> CompareAsync(string fileName, CancellationToken cancellationToken = default);
    Task RestoreAsync(string actor, string fileName, CancellationToken cancellationToken = default);
}
