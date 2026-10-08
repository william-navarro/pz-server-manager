namespace PzServerManager.Application.Mods;

public static class ModInfoParser
{
    public static ModInfo Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        if (!values.TryGetValue("id", out var id) || string.IsNullOrWhiteSpace(id))
        {
            throw new FormatException("O mod.info não contém um id válido.");
        }

        var requires = values.TryGetValue("require", out var requireValue)
            ? requireValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.TrimStart('\\').Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : [];

        return new ModInfo(
            id.Trim(),
            values.GetValueOrDefault("name")?.Trim() is { Length: > 0 } name ? name : id.Trim(),
            requires,
            NullIfEmpty(values.GetValueOrDefault("versionMin")),
            NullIfEmpty(values.GetValueOrDefault("versionMax")),
            NullIfEmpty(values.GetValueOrDefault("modversion")));
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
