using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PzServerManager.Application.Configuration;

namespace PzServerManager.Application.Mods;

public static partial class SandboxOptionsParser
{
    public static IReadOnlyList<SandboxOptionDefinition> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var definitions = new List<SandboxOptionDefinition>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripComment(lines[index]).Trim();
            if (line.Length == 0 || VersionRegex().IsMatch(line)) continue;

            var option = OptionRegex().Match(line);
            if (!option.Success)
                throw Error(index, $"linha inesperada: '{line}'");

            var path = option.Groups["path"].Value;
            var hasOpeningBrace = option.Groups["brace"].Success;
            if (!hasOpeningBrace)
            {
                do
                {
                    index++;
                    if (index >= lines.Length) throw Error(index - 1, $"o bloco de '{path}' não foi aberto");
                    line = StripComment(lines[index]).Trim();
                } while (line.Length == 0);
                if (line != "{") throw Error(index, $"era esperado '{{' depois de '{path}'");
            }

            var body = new StringBuilder();
            var closed = false;
            while (++index < lines.Length)
            {
                line = StripComment(lines[index]).Trim();
                if (line.Length == 0) continue;
                if (line is "}" or "},")
                {
                    closed = true;
                    break;
                }
                if (line.Contains('{') || line.Contains('}'))
                    throw Error(index, $"estrutura aninhada não suportada em '{path}'");
                body.AppendLine(line);
            }
            if (!closed) throw Error(index - 1, $"o bloco de '{path}' não foi fechado");
            if (definitions.Any(item => item.Path.Equals(path, StringComparison.Ordinal)))
                throw Error(index, $"a opção '{path}' aparece mais de uma vez");
            definitions.Add(ParseDefinition(path, body.ToString(), index));
        }

        return definitions;
    }

    private static SandboxOptionDefinition ParseDefinition(string path, string body, int lineIndex)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PropertyRegex().Matches(body))
        {
            var key = match.Groups["key"].Value;
            if (IsKnownProperty(key)) values[key] = DecodeToken(match.Groups["value"].Value.Trim());
        }

        if (!values.TryGetValue("type", out var typeText))
            throw Error(lineIndex, $"a opção '{path}' não declara type");
        if (!values.TryGetValue("default", out var defaultValue))
            throw Error(lineIndex, $"a opção '{path}' não declara default");

        var type = typeText.ToLowerInvariant() switch
        {
            "boolean" => SandboxValueType.Boolean,
            "integer" => SandboxValueType.Integer,
            "double" => SandboxValueType.Double,
            "string" => SandboxValueType.String,
            "enum" => SandboxValueType.Integer,
            _ => throw Error(lineIndex, $"tipo '{typeText}' não suportado em '{path}'")
        };

        var minimum = ParseOptionalDecimal(values, "min", path, lineIndex);
        var maximum = ParseOptionalDecimal(values, "max", path, lineIndex);
        var numberOfValues = ParseOptionalInt(values, "numValues", path, lineIndex);
        ValidateDefault(path, typeText, type, defaultValue, minimum, maximum, numberOfValues, lineIndex);

        return new(path, type, NormalizeDefault(type, defaultValue), minimum, maximum, numberOfValues,
            values.GetValueOrDefault("page"), values.GetValueOrDefault("translation"),
            values.GetValueOrDefault("_tooltip"), values.GetValueOrDefault("valueTranslation"));
    }

    private static void ValidateDefault(
        string path, string declaredType, SandboxValueType type, string value,
        decimal? minimum, decimal? maximum, int? numberOfValues, int lineIndex)
    {
        decimal? number = null;
        if (type == SandboxValueType.Boolean && !bool.TryParse(value, out _))
            throw Error(lineIndex, $"default boolean inválido em '{path}'");
        if (type == SandboxValueType.Integer)
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                throw Error(lineIndex, $"default inteiro inválido em '{path}'");
            number = integer;
        }
        if (type == SandboxValueType.Double)
        {
            if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                throw Error(lineIndex, $"default decimal inválido em '{path}'");
            number = parsed;
        }
        if (number is not null && minimum is not null && number < minimum ||
            number is not null && maximum is not null && number > maximum)
            throw Error(lineIndex, $"default fora dos limites em '{path}'");
        if (declaredType.Equals("enum", StringComparison.OrdinalIgnoreCase))
        {
            if (numberOfValues is null || numberOfValues <= 0)
                throw Error(lineIndex, $"enum '{path}' não declara numValues válido");
            if (number < 1 || number > numberOfValues)
                throw Error(lineIndex, $"default do enum '{path}' está fora de 1..{numberOfValues}");
        }
    }

    private static string NormalizeDefault(SandboxValueType type, string value) => type switch
    {
        SandboxValueType.Boolean => bool.Parse(value) ? "true" : "false",
        SandboxValueType.Integer => long.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        _ => value
    };

    private static decimal? ParseOptionalDecimal(
        IReadOnlyDictionary<string, string> values, string key, string path, int lineIndex)
    {
        if (!values.TryGetValue(key, out var text)) return null;
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw Error(lineIndex, $"{key} inválido em '{path}'");
        return value;
    }

    private static int? ParseOptionalInt(
        IReadOnlyDictionary<string, string> values, string key, string path, int lineIndex)
    {
        if (!values.TryGetValue(key, out var text)) return null;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw Error(lineIndex, $"{key} inválido em '{path}'");
        return value;
    }

    private static bool IsKnownProperty(string key) => key is
        "type" or "default" or "min" or "max" or "numValues" or "page" or
        "translation" or "_tooltip" or "valueTranslation";

    private static string DecodeToken(string token)
    {
        if (token.Length < 2 || token[0] != '"' || token[^1] != '"') return token;
        return token[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string StripComment(string line)
    {
        var quoted = false;
        var escaped = false;
        for (var index = 0; index < line.Length - 1; index++)
        {
            if (escaped) { escaped = false; continue; }
            if (line[index] == '\\' && quoted) { escaped = true; continue; }
            if (line[index] == '"') { quoted = !quoted; continue; }
            if (!quoted && line[index] == '-' && line[index + 1] == '-') return line[..index];
        }
        return line;
    }

    private static FormatException Error(int zeroBasedLine, string message) =>
        new($"sandbox-options.txt inválido próximo da linha {zeroBasedLine + 1}: {message}.");

    [GeneratedRegex(@"^VERSION\s*=\s*\d+\s*,?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
    [GeneratedRegex(@"^option\s+(?<path>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s*(?:=\s*)?(?<brace>\{)?\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex OptionRegex();
    [GeneratedRegex("(?<key>_?[A-Za-z][A-Za-z0-9_]*)\\s*=\\s*(?<value>\"(?:\\\\.|[^\"\\\\])*\"|[^,\\r\\n}]+)\\s*,?", RegexOptions.CultureInvariant)]
    private static partial Regex PropertyRegex();
}
