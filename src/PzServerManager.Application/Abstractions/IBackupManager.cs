using PzServerManager.Application.Backups;

namespace PzServerManager.Application.Abstractions;

public interface IBackupManager
{
    bool IsOperationInProgress { get; }
    string? CurrentOperation { get; }
    Task<BackupOverview> GetOverviewAsync(CancellationToken cancellationToken = default);
    Task<BackupOperationResult> CreateBackupAsync(string actor, CancellationToken cancellationToken = default);
    Task<BackupOperationResult> CreateBackupAsync(string actor, string reason, CancellationToken cancellationToken = default);
    Task<BackupOperationResult> RestoreAsync(string actor, string fileName, CancellationToken cancellationToken = default);
    Task<BackupOperationResult> ResetWorldAsync(string actor, CancellationToken cancellationToken = default);
}
