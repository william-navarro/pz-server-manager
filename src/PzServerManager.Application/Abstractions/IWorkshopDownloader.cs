namespace PzServerManager.Application.Abstractions;

public interface IWorkshopDownloader
{
    Task DownloadAsync(
        IReadOnlyList<string> itemIds,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
