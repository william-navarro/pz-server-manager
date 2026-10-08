namespace PzServerManager.Application.Mods;

public sealed record ModInfo(
    string Id,
    string Name,
    IReadOnlyList<string> Requires,
    string? VersionMin,
    string? VersionMax,
    string? ModVersion);

public sealed record InspectedMod(
    string Id,
    string Name,
    IReadOnlyList<string> Requires,
    string? VersionMin,
    string? VersionMax,
    string? ModVersion,
    string VersionFolder,
    IReadOnlyList<string> MapNames,
    bool IsCompatible,
    string? ContentPath = null);

public sealed record WorkshopItemInspection(
    string ItemId,
    string Path,
    IReadOnlyList<InspectedMod> Mods);

public sealed record WorkshopItemMetadata(
    string ItemId,
    string Title,
    DateTimeOffset? UpdatedAt,
    long? SizeBytes,
    IReadOnlyList<string> Tags);

public enum ManagedModState
{
    Ok,
    ItemNotDownloaded,
    MissingDependency,
    Incompatible
}

public sealed record ManagedMod(
    string Id,
    string Name,
    string? WorkshopItemId,
    string? WorkshopTitle,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> MissingDependencies,
    IReadOnlyList<string> MapNames,
    string? VersionFolder,
    ManagedModState State);

public sealed record ModManagerSnapshot(
    string OriginalText,
    IReadOnlyList<string> WorkshopItems,
    IReadOnlyList<string> ModIds,
    IReadOnlyList<string> Maps,
    IReadOnlyList<ManagedMod> ActiveMods,
    IReadOnlyDictionary<string, WorkshopItemInspection> DownloadedItems,
    IReadOnlyDictionary<string, WorkshopItemMetadata> Metadata);

public sealed record ModConfigurationChange(
    IReadOnlyList<string> WorkshopItemsBefore,
    IReadOnlyList<string> WorkshopItemsAfter,
    IReadOnlyList<string> ModsBefore,
    IReadOnlyList<string> ModsAfter,
    IReadOnlyList<string> MapsBefore,
    IReadOnlyList<string> MapsAfter)
{
    public bool HasChanges =>
        !WorkshopItemsBefore.SequenceEqual(WorkshopItemsAfter, StringComparer.Ordinal) ||
        !ModsBefore.SequenceEqual(ModsAfter, StringComparer.Ordinal) ||
        !MapsBefore.SequenceEqual(MapsAfter, StringComparer.Ordinal);
}

public sealed record DependencyIssue(
    string ModId,
    string RequiredModId,
    IReadOnlyList<string> ProviderItemIds);

public sealed record TopologicalSortResult(
    bool Success,
    IReadOnlyList<string> OrderedModIds,
    IReadOnlyList<string> CycleModIds);
