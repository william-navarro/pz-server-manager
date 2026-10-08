using PzServerManager.Application.Updates;

namespace PzServerManager.Application.Abstractions;

public interface IUpdateManager
{
    bool IsBusy { get; }
    Task<UpdateOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<UpdateOverview> CheckNowAsync(string actor, CancellationToken cancellationToken = default);
    Task ApplyAsync(string actor, UpdateTarget target, CancellationToken cancellationToken = default);
}
