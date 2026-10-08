using PzServerManager.Application.Mods;

namespace PzServerManager.Application.Abstractions;

public interface IWorkshopMetadataClient
{
    Task<IReadOnlyDictionary<string, WorkshopItemMetadata>> GetAsync(
        IReadOnlyList<string> itemIds,
        CancellationToken cancellationToken = default);
}
