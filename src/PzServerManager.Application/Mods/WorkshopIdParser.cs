using System.Globalization;

namespace PzServerManager.Application.Mods;

public static class WorkshopIdParser
{
    public static string Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        if (ulong.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId) && numericId > 0)
        {
            return numericId.ToString(CultureInfo.InvariantCulture);
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (!uri.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase) &&
             !uri.Host.EndsWith(".steamcommunity.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new FormatException($"'{value}' não é um ID nem uma URL válida da Steam Workshop.");
        }

        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || !Uri.UnescapeDataString(part[..separator]).Equals("id", StringComparison.OrdinalIgnoreCase)) continue;
            var id = Uri.UnescapeDataString(part[(separator + 1)..]);
            if (ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out numericId) && numericId > 0)
            {
                return numericId.ToString(CultureInfo.InvariantCulture);
            }
        }

        throw new FormatException($"A URL '{value}' não contém um ID válido da Steam Workshop.");
    }

    public static IReadOnlyList<string> ParseMany(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var values = input.Split(['\r', '\n', '\t', ' ', ',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(values.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var id = Parse(value);
            if (!seen.Add(id)) throw new ArgumentException($"O item da Workshop {id} foi informado mais de uma vez.", nameof(input));
            result.Add(id);
        }
        return result;
    }
}
