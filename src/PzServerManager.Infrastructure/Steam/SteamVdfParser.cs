using System.Text.RegularExpressions;

namespace PzServerManager.Infrastructure.Steam;

public sealed class SteamVdfObject
{
    private readonly Dictionary<string, List<object>> _values = new(StringComparer.OrdinalIgnoreCase);
    internal void Add(string key, object value)
    {
        if (!_values.TryGetValue(key, out var list)) _values[key] = list = [];
        list.Add(value);
    }
    public string? Scalar(string key) => _values.GetValueOrDefault(key)?.OfType<string>().LastOrDefault();
    public SteamVdfObject? Object(string key) => _values.GetValueOrDefault(key)?.OfType<SteamVdfObject>().LastOrDefault();
    public IEnumerable<KeyValuePair<string, SteamVdfObject>> Objects() =>
        _values.SelectMany(pair => pair.Value.OfType<SteamVdfObject>().Select(value => new KeyValuePair<string, SteamVdfObject>(pair.Key, value)));
    public SteamVdfObject? FindObject(string key)
    {
        var direct = Object(key);
        if (direct is not null) return direct;
        foreach (var child in Objects().Select(item => item.Value))
        {
            var found = child.FindObject(key);
            if (found is not null) return found;
        }
        return null;
    }
    public string? FindScalar(string key)
    {
        var direct = Scalar(key);
        if (direct is not null) return direct;
        foreach (var child in Objects().Select(item => item.Value))
        {
            var found = child.FindScalar(key);
            if (found is not null) return found;
        }
        return null;
    }
}

public static partial class SteamVdfParser
{
    public static SteamVdfObject Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = TokenPattern().Matches(text).Select(match => match.Groups[1].Success
            ? Regex.Unescape(match.Groups[1].Value) : match.Groups[2].Value).ToArray();
        var index = 0;
        return ParseObject(tokens, ref index, false);
    }

    public static IReadOnlyDictionary<string, DateTimeOffset?> ParseWorkshopUpdates(string text)
    {
        var root = Parse(text);
        var details = root.FindObject("WorkshopItemsInstalled") ?? root.FindObject("WorkshopItemDetails");
        if (details is null) return new Dictionary<string, DateTimeOffset?>();
        return details.Objects().Where(item => ulong.TryParse(item.Key, out _)).ToDictionary(
            item => item.Key,
            item => long.TryParse(item.Value.Scalar("timeupdated"), out var seconds) && seconds > 0
                ? (DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
            StringComparer.Ordinal);
    }

    public static string? ParseInstalledBuild(string text) => Parse(text).FindScalar("buildid");

    public static string? ParsePublicBuild(string text)
    {
        var branches = Parse(text).FindObject("branches");
        return branches?.Object("public")?.Scalar("buildid");
    }

    private static SteamVdfObject ParseObject(string[] tokens, ref int index, bool expectClosing)
    {
        var result = new SteamVdfObject();
        while (index < tokens.Length)
        {
            if (tokens[index] == "}") { index++; break; }
            var key = tokens[index++];
            if (index >= tokens.Length) break;
            if (tokens[index] == "{")
            {
                index++;
                result.Add(key, ParseObject(tokens, ref index, true));
            }
            else result.Add(key, tokens[index++]);
        }
        if (expectClosing && index > tokens.Length) throw new FormatException("Manifesto VDF incompleto.");
        return result;
    }

    [GeneratedRegex("\"((?:\\\\.|[^\"\\\\])*)\"|([{}])", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
