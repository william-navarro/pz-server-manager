using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Configuration;

namespace PzServerManager.Application.Mods;

public sealed class ModManagerService(
    IServerIniStore store,
    IManagerPaths paths,
    IWorkshopMetadataClient metadataClient,
    IAuditLog auditLog,
    WorkshopItemInspector inspector)
{
    public string WorkshopContentPath => Path.Combine(paths.DataRoot, "workshop", "steamapps", "workshop", "content", "108600");

    public async Task<ModManagerSnapshot> LoadAsync(
        string iniPath,
        string serverVersion = "42.21",
        CancellationToken cancellationToken = default)
    {
        var document = await store.LoadAsync(iniPath, cancellationToken);
        var workshopItems = document.GetList("WorkshopItems");
        var modIds = document.GetList("Mods");
        var maps = document.GetList("Map");
        var downloaded = InspectDownloaded(serverVersion);
        IReadOnlyDictionary<string, WorkshopItemMetadata> metadata;
        try
        {
            metadata = await metadataClient.GetAsync(workshopItems, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            metadata = new Dictionary<string, WorkshopItemMetadata>(StringComparer.Ordinal);
        }

        var activeMods = BuildActiveMods(workshopItems, modIds, downloaded, metadata);
        return new(document.ToText(), workshopItems, modIds, maps, activeMods, downloaded, metadata);
    }

    public WorkshopItemInspection InspectItem(string itemId, string serverVersion = "42.21")
    {
        var normalizedId = WorkshopIdParser.Parse(itemId);
        var path = Path.Combine(WorkshopContentPath, normalizedId);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"O item {normalizedId} não foi baixado pelo painel.");
        return inspector.Inspect(normalizedId, path, serverVersion);
    }

    public ModConfigurationChange BuildChange(
        ModManagerSnapshot snapshot,
        IReadOnlyList<string> workshopItems,
        IReadOnlyList<string> modIds,
        IReadOnlyList<string> maps)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var knownItems = snapshot.DownloadedItems.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var pair in InspectDownloaded()) knownItems[pair.Key] = pair.Value;
        ValidateConfiguration(workshopItems, modIds, maps, knownItems.Values);
        return new(snapshot.WorkshopItems, workshopItems.ToArray(), snapshot.ModIds, modIds.ToArray(), snapshot.Maps, maps.ToArray());
    }

    public async Task<ModConfigurationChange> SaveAsync(
        string iniPath,
        ModManagerSnapshot snapshot,
        IReadOnlyList<string> workshopItems,
        IReadOnlyList<string> modIds,
        IReadOnlyList<string> maps,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var change = BuildChange(snapshot, workshopItems, modIds, maps);
        if (!change.HasChanges) return change;

        var current = await store.LoadAsync(iniPath, cancellationToken);
        if (!string.Equals(current.ToText(), snapshot.OriginalText, StringComparison.Ordinal))
        {
            throw new ServerSettingsConflictException("O arquivo foi alterado no disco. Recarregue a página antes de salvar.");
        }

        current.SetList("WorkshopItems", workshopItems);
        current.SetList("Mods", modIds);
        current.SetList("Map", maps);
        await store.SaveAsync(iniPath, current, cancellationToken);
        await auditLog.WriteAsync(
            actor,
            "alterar mods",
            $"WorkshopItems: {Format(change.WorkshopItemsBefore)} -> {Format(change.WorkshopItemsAfter)}; " +
            $"Mods: {Format(change.ModsBefore)} -> {Format(change.ModsAfter)}; " +
            $"Map: {Format(change.MapsBefore)} -> {Format(change.MapsAfter)}",
            CancellationToken.None);
        return change;
    }

    public static IReadOnlyList<string> SetMapEnabled(
        IReadOnlyList<string> currentMaps,
        IEnumerable<string> modMaps,
        bool enabled)
    {
        var affected = modMaps.ToHashSet(StringComparer.Ordinal);
        var result = currentMaps.Where(map => !affected.Contains(map)).ToList();
        if (!enabled) return result;

        var anchor = result.FindIndex(map => map.Equals("Muldraugh, KY", StringComparison.Ordinal));
        if (anchor < 0) anchor = result.Count;
        foreach (var map in affected.OrderBy(map => map, StringComparer.Ordinal)) result.Insert(anchor++, map);
        return result;
    }

    private Dictionary<string, WorkshopItemInspection> InspectDownloaded(string serverVersion = "42.21")
    {
        var result = new Dictionary<string, WorkshopItemInspection>(StringComparer.Ordinal);
        if (!Directory.Exists(WorkshopContentPath)) return result;
        foreach (var directory in Directory.GetDirectories(WorkshopContentPath))
        {
            var id = Path.GetFileName(directory);
            if (!ulong.TryParse(id, out _)) continue;
            result[id] = inspector.Inspect(id, directory, serverVersion);
        }
        return result;
    }

    private static IReadOnlyList<ManagedMod> BuildActiveMods(
        IReadOnlyList<string> workshopItems,
        IReadOnlyList<string> modIds,
        IReadOnlyDictionary<string, WorkshopItemInspection> downloaded,
        IReadOnlyDictionary<string, WorkshopItemMetadata> metadata)
    {
        var activeSet = modIds.ToHashSet(StringComparer.Ordinal);
        var activeItems = workshopItems.Where(downloaded.ContainsKey).Select(id => downloaded[id]).ToArray();
        var missingItemIds = workshopItems.Where(id => !downloaded.ContainsKey(id)).ToArray();
        var allKnownMods = activeItems.SelectMany(item => item.Mods.Select(mod => (Item: item, Mod: mod))).ToArray();
        return modIds.Select(id =>
        {
            var match = allKnownMods.FirstOrDefault(entry => entry.Mod.Id.Equals(id, StringComparison.Ordinal));
            if (match.Mod is null)
            {
                var origin = missingItemIds.Length == 1 ? missingItemIds[0] : null;
                metadata.TryGetValue(origin ?? string.Empty, out var missingMetadata);
                return new ManagedMod(id, id, origin, missingMetadata?.Title, [], [], [], null, ManagedModState.ItemNotDownloaded);
            }
            var missing = match.Mod.Requires.Where(required => !activeSet.Contains(required)).ToArray();
            var state = !match.Mod.IsCompatible ? ManagedModState.Incompatible :
                missing.Length > 0 ? ManagedModState.MissingDependency : ManagedModState.Ok;
            metadata.TryGetValue(match.Item.ItemId, out var itemMetadata);
            return new ManagedMod(id, match.Mod.Name, match.Item.ItemId, itemMetadata?.Title, match.Mod.Requires,
                missing, match.Mod.MapNames, match.Mod.VersionFolder, state);
        }).ToArray();
    }

    private static void ValidateConfiguration(
        IReadOnlyList<string> workshopItems,
        IReadOnlyList<string> modIds,
        IReadOnlyList<string> maps,
        IEnumerable<WorkshopItemInspection> knownItems)
    {
        foreach (var id in workshopItems)
        {
            if (!string.Equals(WorkshopIdParser.Parse(id), id, StringComparison.Ordinal))
                throw new ArgumentException($"ID de Workshop inválido: {id}.");
        }
        ModDependencyResolver.EnsureUnique(workshopItems, "Item da Workshop");
        ModDependencyResolver.EnsureUnique(modIds, "Mod ID");
        ModDependencyResolver.EnsureUnique(maps, "Mapa");

        var activeItems = knownItems.Where(item => workshopItems.Contains(item.ItemId, StringComparer.Ordinal)).ToArray();
        var duplicateProvider = activeItems.SelectMany(item => item.Mods.Select(mod => (item.ItemId, mod.Id)))
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateProvider is not null)
        {
            throw new ArgumentException($"O Mod ID {duplicateProvider.Key} existe em mais de um item da Workshop selecionado.");
        }
    }

    private static string Format(IEnumerable<string> values) => string.Join(';', values);
}
