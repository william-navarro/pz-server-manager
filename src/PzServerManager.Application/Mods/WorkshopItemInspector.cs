namespace PzServerManager.Application.Mods;

public sealed class WorkshopItemInspector
{
    public WorkshopItemInspection Inspect(string itemId, string itemPath, string serverVersion = "42.21")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        if (!PzVersion.TryParse(serverVersion, out var targetVersion))
        {
            throw new ArgumentException("A versão do servidor é inválida.", nameof(serverVersion));
        }

        var fullPath = Path.GetFullPath(itemPath);
        var modsPath = Path.Combine(fullPath, "mods");
        if (!Directory.Exists(modsPath))
        {
            return new(itemId, fullPath, []);
        }

        var mods = new List<InspectedMod>();
        foreach (var modDirectory in Directory.GetDirectories(modsPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var candidate = SelectModInfo(modDirectory, targetVersion);
            if (candidate is null) continue;

            ModInfo info;
            try
            {
                info = ModInfoParser.Parse(File.ReadAllText(candidate.Value.InfoPath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                throw new InvalidOperationException($"Não foi possível ler '{candidate.Value.InfoPath}': {exception.Message}", exception);
            }

            var mapNames = FindMaps(modDirectory, candidate.Value.ContentRoot, candidate.Value.HasVersionDirectories);
            mods.Add(new(
                info.Id,
                info.Name,
                info.Requires,
                info.VersionMin,
                info.VersionMax,
                info.ModVersion,
                candidate.Value.Label,
                mapNames,
                IsCompatible(info, targetVersion),
                candidate.Value.ContentRoot));
        }

        return new(itemId, fullPath, mods);
    }

    private static Candidate? SelectModInfo(string modDirectory, PzVersion targetVersion)
    {
        var versionDirectories = Directory.GetDirectories(modDirectory)
            .Select(path => (Path: path, Name: Path.GetFileName(path)))
            .Where(item => PzVersion.TryParse(item.Name, out _))
            .Select(item => (item.Path, item.Name, Version: ParseVersion(item.Name)))
            .OrderByDescending(item => item.Version)
            .ToArray();

        var selected = versionDirectories.FirstOrDefault(item => item.Version.CompareTo(targetVersion) <= 0);
        if (selected.Path is not null)
        {
            var direct = Path.Combine(selected.Path, "mod.info");
            var nestedCommon = Path.Combine(selected.Path, "common", "mod.info");
            if (File.Exists(direct)) return new(direct, selected.Path, selected.Name, true);
            if (File.Exists(nestedCommon)) return new(nestedCommon, Path.Combine(selected.Path, "common"), selected.Name, true);
        }

        var commonPath = Path.Combine(modDirectory, "common", "mod.info");
        if (File.Exists(commonPath)) return new(commonPath, Path.Combine(modDirectory, "common"), "common", versionDirectories.Length > 0);

        var rootPath = Path.Combine(modDirectory, "mod.info");
        if (versionDirectories.Length == 0 && File.Exists(rootPath))
        {
            return new(rootPath, modDirectory, "raiz", false);
        }

        return null;
    }

    private static IReadOnlyList<string> FindMaps(string modDirectory, string contentRoot, bool hasVersions)
    {
        var roots = new List<string> { contentRoot };
        var nestedCommon = Path.Combine(contentRoot, "common");
        if (Directory.Exists(nestedCommon)) roots.Add(nestedCommon);
        var common = Path.Combine(modDirectory, "common");
        if (Directory.Exists(common) && !roots.Contains(common, StringComparer.OrdinalIgnoreCase)) roots.Add(common);
        if (!hasVersions && !roots.Contains(modDirectory, StringComparer.OrdinalIgnoreCase)) roots.Add(modDirectory);

        return roots.SelectMany(root =>
            {
                var maps = Path.Combine(root, "media", "maps");
                return Directory.Exists(maps) ? Directory.GetDirectories(maps).Select(Path.GetFileName) : [];
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsCompatible(ModInfo info, PzVersion serverVersion)
    {
        if (PzVersion.TryParse(info.VersionMin, out var minimum) && serverVersion.CompareTo(minimum) < 0) return false;
        if (PzVersion.TryParse(info.VersionMax, out var maximum) && serverVersion.CompareTo(maximum) > 0) return false;
        return true;
    }

    private static PzVersion ParseVersion(string value)
    {
        PzVersion.TryParse(value, out var version);
        return version;
    }

    private readonly record struct Candidate(
        string InfoPath,
        string ContentRoot,
        string Label,
        bool HasVersionDirectories);
}
