namespace PzServerManager.Application.Mods;

public static class ModDependencyResolver
{
    public static IReadOnlyList<DependencyIssue> FindMissing(
        IReadOnlyList<string> activeModIds,
        IEnumerable<WorkshopItemInspection> knownItems)
    {
        ArgumentNullException.ThrowIfNull(activeModIds);
        ArgumentNullException.ThrowIfNull(knownItems);
        var items = knownItems.ToArray();
        var active = activeModIds.ToHashSet(StringComparer.Ordinal);
        var mods = items.SelectMany(item => item.Mods.Select(mod => (Item: item, Mod: mod)))
            .GroupBy(entry => entry.Mod.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Mod, StringComparer.Ordinal);
        var issues = new List<DependencyIssue>();

        foreach (var modId in activeModIds.Distinct(StringComparer.Ordinal))
        {
            if (!mods.TryGetValue(modId, out var mod)) continue;
            foreach (var required in mod.Requires.Where(required => !active.Contains(required)))
            {
                var providers = items.Where(item => item.Mods.Any(candidate => candidate.Id.Equals(required, StringComparison.Ordinal)))
                    .Select(item => item.ItemId)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                issues.Add(new(modId, required, providers));
            }
        }
        return issues;
    }

    public static TopologicalSortResult Sort(
        IReadOnlyList<string> activeModIds,
        IEnumerable<WorkshopItemInspection> knownItems)
    {
        ArgumentNullException.ThrowIfNull(activeModIds);
        var items = knownItems.ToArray();
        EnsureUnique(activeModIds, "Mod ID");
        var active = activeModIds.ToHashSet(StringComparer.Ordinal);
        var index = activeModIds.Select((id, position) => (id, position))
            .ToDictionary(entry => entry.id, entry => entry.position, StringComparer.Ordinal);
        var info = items.SelectMany(item => item.Mods)
            .GroupBy(mod => mod.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var indegree = activeModIds.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var outgoing = activeModIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var modId in activeModIds)
        {
            if (!info.TryGetValue(modId, out var mod)) continue;
            foreach (var dependency in mod.Requires.Where(active.Contains).Distinct(StringComparer.Ordinal))
            {
                outgoing[dependency].Add(modId);
                indegree[modId]++;
            }
        }

        var ready = new SortedSet<(int Index, string Id)>(Comparer<(int Index, string Id)>.Create((left, right) =>
        {
            var byIndex = left.Index.CompareTo(right.Index);
            return byIndex != 0 ? byIndex : StringComparer.Ordinal.Compare(left.Id, right.Id);
        }));
        foreach (var modId in activeModIds.Where(id => indegree[id] == 0)) ready.Add((index[modId], modId));

        var ordered = new List<string>(activeModIds.Count);
        while (ready.Count > 0)
        {
            var next = ready.Min;
            ready.Remove(next);
            ordered.Add(next.Id);
            foreach (var dependent in outgoing[next.Id])
            {
                if (--indegree[dependent] == 0) ready.Add((index[dependent], dependent));
            }
        }

        if (ordered.Count == activeModIds.Count) return new(true, ordered, []);
        return new(false, activeModIds, activeModIds.Where(id => indegree[id] > 0).ToArray());
    }

    public static IReadOnlySet<string> SelectDefaults(WorkshopItemInspection item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var localIds = item.Mods.Select(mod => mod.Id).ToHashSet(StringComparer.Ordinal);
        var roots = item.Mods.Where(mod => !mod.Requires.Any(localIds.Contains)).ToArray();
        var selected = roots.Where(mod => !LooksLikeBase(mod)).Select(mod => mod.Id).ToHashSet(StringComparer.Ordinal);

        if (selected.Count == 0)
        {
            var dependentsOfBase = item.Mods.Where(mod => mod.Requires.Any(required => roots.Any(root => root.Id == required))).ToArray();
            foreach (var mod in dependentsOfBase.Length > 0 ? dependentsOfBase : roots) selected.Add(mod.Id);
        }

        AddRequiredClosure(selected, item.Mods);
        return selected;
    }

    public static IReadOnlyList<string> FindDependents(
        IEnumerable<string> removedModIds,
        IReadOnlyList<string> remainingModIds,
        IEnumerable<WorkshopItemInspection> knownItems)
    {
        var removed = removedModIds.ToHashSet(StringComparer.Ordinal);
        var remaining = remainingModIds.ToHashSet(StringComparer.Ordinal);
        return knownItems.SelectMany(item => item.Mods)
            .Where(mod => remaining.Contains(mod.Id) && mod.Requires.Any(removed.Contains))
            .Select(mod => mod.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddRequiredClosure(HashSet<string> selected, IReadOnlyList<InspectedMod> mods)
    {
        var byId = mods.ToDictionary(mod => mod.Id, StringComparer.Ordinal);
        var pending = new Queue<string>(selected);
        while (pending.TryDequeue(out var id))
        {
            if (!byId.TryGetValue(id, out var mod)) continue;
            foreach (var required in mod.Requires.Where(byId.ContainsKey))
            {
                if (selected.Add(required)) pending.Enqueue(required);
            }
        }
    }

    private static bool LooksLikeBase(InspectedMod mod) =>
        mod.Id.Contains("base", StringComparison.OrdinalIgnoreCase) ||
        mod.Name.Contains("[base]", StringComparison.OrdinalIgnoreCase) ||
        mod.Name.EndsWith(" base", StringComparison.OrdinalIgnoreCase);

    public static void EnsureUnique(IEnumerable<string> values, string label)
    {
        var duplicate = values.GroupBy(value => value, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new ArgumentException($"{label} duplicado: {duplicate.Key}.");
    }
}
