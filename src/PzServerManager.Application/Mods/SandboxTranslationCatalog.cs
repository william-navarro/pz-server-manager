using System.Text.Json;
using System.Text.RegularExpressions;
using PzServerManager.Application.Configuration;

namespace PzServerManager.Application.Mods;

public sealed partial class SandboxTranslationCatalog
{
    private readonly IReadOnlyDictionary<string, string> _english;
    private readonly IReadOnlyDictionary<string, string> _values;

    public SandboxTranslationCatalog(IReadOnlyDictionary<string, string> english, IReadOnlyDictionary<string, string> ptBr)
    {
        ArgumentNullException.ThrowIfNull(english);
        ArgumentNullException.ThrowIfNull(ptBr);
        _english = new Dictionary<string, string>(english, StringComparer.Ordinal);
        var merged = new Dictionary<string, string>(english, StringComparer.Ordinal);
        foreach (var (key, value) in ptBr) merged[key] = value;
        _values = merged;
    }

    public string? Get(string key)
        => Get(_values, key);

    public string? GetEnglish(string key)
        => Get(_english, key);

    private static string? Get(IReadOnlyDictionary<string, string> values, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var normalized = key.StartsWith("Sandbox_", StringComparison.Ordinal) ? key : $"Sandbox_{key}";
        return values.TryGetValue(normalized, out var value) ? Normalize(value) : null;
    }

    public TranslatedSandboxOption Translate(SandboxOptionDefinition definition)
    {
        var translation = definition.Translation ?? definition.Path;
        var label = Get(translation) ?? definition.Path[(definition.Path.LastIndexOf('.') + 1)..];
        var tooltip = Get($"{translation}_tooltip") ?? Get(definition.TooltipTranslation ?? string.Empty);
        var page = Get(definition.Page ?? string.Empty) ?? definition.Page ?? "Opções";
        var enumOptions = new List<SandboxEnumOption>();
        if (definition.NumberOfValues is not null)
        {
            var stem = definition.ValueTranslation ?? translation;
            for (var value = 1; value <= definition.NumberOfValues; value++)
                enumOptions.Add(new(value, Get($"{stem}_option{value}") ?? value.ToString()));
        }
        return new(label, tooltip, page, enumOptions);
    }

    public static SandboxTranslationCatalog LoadFromContentRoot(string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        var translateRoot = Path.Combine(contentRoot, "media", "lua", "shared", "Translate");
        return new(LoadLanguage(translateRoot, "EN"), LoadLanguage(translateRoot, "PTBR"));
    }

    public static SandboxTranslationCatalog LoadVanilla(string installPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installPath);
        var translateRoot = Path.Combine(installPath, "media", "lua", "shared", "Translate");
        return new(LoadLanguage(translateRoot, "EN"), LoadLanguage(translateRoot, "PTBR"));
    }

    public static IReadOnlyDictionary<string, string> ParseJson(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException("A tradução Sandbox.json deve ser um objeto JSON.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.String)
                result[property.Name] = Normalize(property.Value.GetString() ?? string.Empty);
        return result;
    }

    public static IReadOnlyDictionary<string, string> ParseLegacy(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var match = LegacyEntryRegex().Match(rawLine);
            if (!match.Success) continue;
            var value = match.Groups["value"].Value
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
            result[match.Groups["key"].Value] = Normalize(value);
        }
        return result;
    }

    private static Dictionary<string, string> LoadLanguage(string translateRoot, string language)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var directory = Path.Combine(translateRoot, language);
        if (!Directory.Exists(directory)) return result;
        foreach (var path in Directory.GetFiles(directory, "Sandbox*.txt").OrderBy(path => path, StringComparer.Ordinal))
            foreach (var pair in ParseLegacy(File.ReadAllText(path))) result[pair.Key] = pair.Value;
        foreach (var path in Directory.GetFiles(directory, "Sandbox*.json").OrderBy(path => path, StringComparer.Ordinal))
            foreach (var pair in ParseJson(File.ReadAllText(path))) result[pair.Key] = pair.Value;
        return result;
    }

    private static string Normalize(string value) => value
        .Replace("%%", "%", StringComparison.Ordinal)
        .Replace("\\n", "\n", StringComparison.Ordinal)
        .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
        .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^\\s*(?<key>Sandbox_[A-Za-z0-9_.]+)\\s*=\\s*\"(?<value>.*)\"\\s*,?\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyEntryRegex();
}
