using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PzServerManager.Application.Configuration;

public enum SandboxValueType
{
    Boolean,
    Integer,
    Double,
    String
}

public sealed record SandboxEnumOption(int Value, string Label);

public sealed record SandboxVarsEntry(
    string Path,
    string Name,
    string? Group,
    string Value,
    string RawValue,
    SandboxValueType ValueType,
    string? Description,
    IReadOnlyList<SandboxEnumOption> EnumOptions,
    decimal? Minimum,
    decimal? Maximum,
    string? DefaultValue)
{
    public bool IsChangedFromDefault =>
        DefaultValue is not null && !ValuesEqual(Value, DefaultValue, ValueType);

    private static bool ValuesEqual(string left, string right, SandboxValueType type)
    {
        if (type is SandboxValueType.Integer or SandboxValueType.Double &&
            decimal.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber) &&
            decimal.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(left, right, StringComparison.Ordinal);
    }
}

/// <summary>
/// Editor conservador para o formato de SandboxVars emitido pelo Project Zomboid.
/// Não é um interpretador Lua: estruturas fora de uma tabela raiz e um nível de grupo são recusadas.
/// </summary>
public sealed partial class SandboxVarsDocument
{
    private readonly List<Line> _lines;
    private readonly List<EntryData> _entries;
    private readonly string _originalText;

    private SandboxVarsDocument(List<Line> lines, List<EntryData> entries, string originalText)
    {
        _lines = lines;
        _entries = entries;
        _originalText = originalText;
    }

    public IReadOnlyList<SandboxVarsEntry> Entries => _entries.Select(CreateEntry).ToArray();
    public bool HasChanges => !string.Equals(_originalText, ToText(), StringComparison.Ordinal);

    public static SandboxVarsDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = SplitLines(text);
        var entries = new List<EntryData>();
        var started = false;
        var finished = false;
        string? group = null;
        var pendingComments = new List<string>();

        for (var index = 0; index < lines.Count; index++)
        {
            var content = lines[index].Content;
            var trimmed = content.Trim();

            if (!started)
            {
                if (trimmed.Length == 0) continue;
                if (!RootStartRegex().IsMatch(content))
                    throw FormatError(index, "era esperado 'SandboxVars = {'");
                started = true;
                continue;
            }

            if (finished)
            {
                if (trimmed.Length != 0) throw FormatError(index, "há conteúdo depois do fim da tabela");
                continue;
            }

            if (trimmed.Length == 0)
            {
                pendingComments.Clear();
                continue;
            }

            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                pendingComments.Add(trimmed[2..].TrimStart());
                continue;
            }

            if (group is null && trimmed == "}")
            {
                finished = true;
                pendingComments.Clear();
                continue;
            }

            if (group is not null && trimmed is "}," or "}")
            {
                group = null;
                pendingComments.Clear();
                continue;
            }

            var groupMatch = GroupStartRegex().Match(content);
            if (groupMatch.Success)
            {
                if (group is not null) throw FormatError(index, "grupos aninhados não são suportados");
                group = groupMatch.Groups["key"].Value;
                pendingComments.Clear();
                continue;
            }

            var assignment = AssignmentRegex().Match(content);
            if (!assignment.Success)
                throw FormatError(index, $"sintaxe inesperada: '{trimmed}'");

            var indent = assignment.Groups["indent"].Value.Length;
            if ((group is null && indent == 0) || (group is not null && indent == 0))
                throw FormatError(index, "a atribuição deve estar recuada dentro da tabela");

            var key = assignment.Groups["key"].Value;
            var rawValue = assignment.Groups["value"].Value;
            var valueType = DetectType(rawValue, index);
            var path = group is null ? key : $"{group}.{key}";
            if (entries.Any(entry => entry.Path.Equals(path, StringComparison.Ordinal)))
                throw FormatError(index, $"a chave '{path}' aparece mais de uma vez");

            var metadata = ParseComments(pendingComments, valueType);
            entries.Add(new EntryData(
                path, key, group, index,
                assignment.Groups["value"].Index,
                assignment.Groups["value"].Length,
                valueType,
                metadata.Description,
                metadata.EnumOptions,
                metadata.Minimum,
                metadata.Maximum,
                metadata.DefaultValue));
            pendingComments.Clear();
        }

        if (!started) throw new FormatException("O arquivo SandboxVars está vazio.");
        if (group is not null) throw new FormatException($"O grupo '{group}' não foi fechado.");
        if (!finished) throw new FormatException("A tabela SandboxVars não foi fechada.");
        return new SandboxVarsDocument(lines, entries, text);
    }

    public string? Get(string path)
    {
        var entry = Find(path, required: false);
        return entry is null ? null : DecodeValue(CurrentRawValue(entry), entry.ValueType);
    }

    public string? GetRaw(string path)
    {
        var entry = Find(path, required: false);
        return entry is null ? null : CurrentRawValue(entry);
    }

    public void Set(string path, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var entry = Find(path, required: true)!;
        if (value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("O valor não pode conter quebra de linha.", nameof(value));

        var normalized = NormalizeValue(value, entry);
        var line = _lines[entry.LineIndex];
        line.Content = string.Concat(
            line.Content.AsSpan(0, entry.ValueStart),
            normalized,
            line.Content.AsSpan(entry.ValueStart + entry.ValueLength));
        entry.ValueLength = normalized.Length;
    }

    public string ToText()
    {
        var length = _lines.Sum(line => line.Content.Length + line.Terminator.Length);
        return string.Create(length, _lines, static (span, lines) =>
        {
            var offset = 0;
            foreach (var line in lines)
            {
                line.Content.AsSpan().CopyTo(span[offset..]);
                offset += line.Content.Length;
                line.Terminator.AsSpan().CopyTo(span[offset..]);
                offset += line.Terminator.Length;
            }
        });
    }

    private SandboxVarsEntry CreateEntry(EntryData data)
    {
        var raw = CurrentRawValue(data);
        return new(data.Path, data.Name, data.Group, DecodeValue(raw, data.ValueType), raw,
            data.ValueType, data.Description, data.EnumOptions, data.Minimum, data.Maximum, data.DefaultValue);
    }

    private EntryData? Find(string path, bool required)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var entry = _entries.FirstOrDefault(item => item.Path.Equals(path, StringComparison.Ordinal));
        if (entry is null && required) throw new KeyNotFoundException($"A opção de mundo '{path}' não existe.");
        return entry;
    }

    private string CurrentRawValue(EntryData entry) =>
        _lines[entry.LineIndex].Content.Substring(entry.ValueStart, entry.ValueLength);

    private static string NormalizeValue(string value, EntryData entry)
    {
        var trimmed = value.Trim();
        switch (entry.ValueType)
        {
            case SandboxValueType.Boolean:
                if (!bool.TryParse(trimmed, out var boolean))
                    throw InvalidValue(entry.Path, "use true ou false");
                return boolean ? "true" : "false";

            case SandboxValueType.Integer:
                if (!long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                    throw InvalidValue(entry.Path, "use um número inteiro");
                ValidateRange(entry, integer);
                if (entry.EnumOptions.Count > 0 && entry.EnumOptions.All(option => option.Value != integer))
                    throw InvalidValue(entry.Path, "selecione uma das opções enumeradas");
                return integer.ToString(CultureInfo.InvariantCulture);

            case SandboxValueType.Double:
                if (!decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw InvalidValue(entry.Path, "use um número decimal com ponto");
                ValidateRange(entry, number);
                return trimmed;

            case SandboxValueType.String:
                if (trimmed.StartsWith('"'))
                {
                    _ = DecodeLuaString(trimmed);
                    return trimmed;
                }
                return EncodeLuaString(value);

            default:
                throw new InvalidOperationException("Tipo de SandboxVars desconhecido.");
        }
    }

    private static void ValidateRange(EntryData entry, decimal number)
    {
        if (entry.Minimum is not null && number < entry.Minimum)
            throw InvalidValue(entry.Path, $"o mínimo é {entry.Minimum.Value.ToString(CultureInfo.InvariantCulture)}");
        if (entry.Maximum is not null && number > entry.Maximum)
            throw InvalidValue(entry.Path, $"o máximo é {entry.Maximum.Value.ToString(CultureInfo.InvariantCulture)}");
    }

    private static ArgumentException InvalidValue(string path, string detail) =>
        new($"Valor inválido para '{path}': {detail}.");

    private static SandboxValueType DetectType(string raw, int lineIndex)
    {
        if (raw is "true" or "false") return SandboxValueType.Boolean;
        if (IntegerRegex().IsMatch(raw)) return SandboxValueType.Integer;
        if (DoubleRegex().IsMatch(raw)) return SandboxValueType.Double;
        if (raw.StartsWith('"') && raw.EndsWith('"'))
        {
            _ = DecodeLuaString(raw);
            return SandboxValueType.String;
        }
        throw FormatError(lineIndex, $"tipo de valor não suportado: '{raw}'");
    }

    private static string DecodeValue(string raw, SandboxValueType type) =>
        type == SandboxValueType.String ? DecodeLuaString(raw) : raw;

    private static string DecodeLuaString(string raw)
    {
        if (raw.Length < 2 || raw[0] != '"' || raw[^1] != '"')
            throw new FormatException($"String Lua inválida: '{raw}'.");
        var result = new StringBuilder(raw.Length - 2);
        for (var index = 1; index < raw.Length - 1; index++)
        {
            var character = raw[index];
            if (character != '\\')
            {
                if (character == '"') throw new FormatException($"Aspas não escapadas em '{raw}'.");
                result.Append(character);
                continue;
            }

            if (++index >= raw.Length - 1) throw new FormatException($"Escape incompleto em '{raw}'.");
            result.Append(raw[index] switch
            {
                'n' => '\n', 'r' => '\r', 't' => '\t', '"' => '"', '\\' => '\\',
                var escaped => escaped
            });
        }
        return result.ToString();
    }

    private static string EncodeLuaString(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static CommentMetadata ParseComments(IReadOnlyList<string> comments, SandboxValueType type)
    {
        var enumOptions = new List<SandboxEnumOption>();
        decimal? minimum = null, maximum = null;
        string? defaultValue = null;
        var description = new List<string>();

        foreach (var comment in comments)
        {
            var enumMatch = EnumCommentRegex().Match(comment);
            if (enumMatch.Success)
            {
                enumOptions.Add(new(
                    int.Parse(enumMatch.Groups["value"].Value, CultureInfo.InvariantCulture),
                    enumMatch.Groups["label"].Value.Trim()));
                continue;
            }

            var constraints = ConstraintsRegex().Match(comment);
            if (constraints.Success)
            {
                minimum = decimal.Parse(constraints.Groups["min"].Value, CultureInfo.InvariantCulture);
                maximum = decimal.Parse(constraints.Groups["max"].Value, CultureInfo.InvariantCulture);
                defaultValue = constraints.Groups["default"].Value;
                var withoutConstraints = comment.Remove(constraints.Index, constraints.Length).Trim();
                if (withoutConstraints.Length > 0) description.Add(withoutConstraints);
                continue;
            }

            description.Add(comment);
            var defaultMatch = NamedDefaultRegex().Match(comment);
            if (defaultMatch.Success)
                defaultValue = defaultMatch.Groups["default"].Value.Trim();
        }

        if (defaultValue is not null && type == SandboxValueType.Integer && enumOptions.Count > 0)
        {
            var matchingOption = enumOptions.FirstOrDefault(option =>
                option.Label.Equals(defaultValue, StringComparison.OrdinalIgnoreCase));
            if (matchingOption is not null)
                defaultValue = matchingOption.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (defaultValue is not null && type == SandboxValueType.Boolean)
            defaultValue = defaultValue.ToLowerInvariant();

        return new(
            description.Count == 0 ? null : string.Join('\n', description),
            enumOptions,
            minimum,
            maximum,
            defaultValue);
    }

    private static List<Line> SplitLines(string text)
    {
        var lines = new List<Line>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n') continue;
            var crlf = index > start && text[index - 1] == '\r';
            lines.Add(new(text.Substring(start, index - start - (crlf ? 1 : 0)), crlf ? "\r\n" : "\n"));
            start = index + 1;
        }
        if (start < text.Length) lines.Add(new(text[start..], string.Empty));
        return lines;
    }

    private static FormatException FormatError(int zeroBasedLine, string message) =>
        new($"SandboxVars inválido na linha {zeroBasedLine + 1}: {message}.");

    private sealed class Line(string content, string terminator)
    {
        public string Content { get; set; } = content;
        public string Terminator { get; } = terminator;
    }

    private sealed class EntryData(
        string path, string name, string? group, int lineIndex, int valueStart, int valueLength,
        SandboxValueType valueType, string? description, IReadOnlyList<SandboxEnumOption> enumOptions,
        decimal? minimum, decimal? maximum, string? defaultValue)
    {
        public string Path { get; } = path;
        public string Name { get; } = name;
        public string? Group { get; } = group;
        public int LineIndex { get; } = lineIndex;
        public int ValueStart { get; } = valueStart;
        public int ValueLength { get; set; } = valueLength;
        public SandboxValueType ValueType { get; } = valueType;
        public string? Description { get; } = description;
        public IReadOnlyList<SandboxEnumOption> EnumOptions { get; } = enumOptions;
        public decimal? Minimum { get; } = minimum;
        public decimal? Maximum { get; } = maximum;
        public string? DefaultValue { get; } = defaultValue;
    }

    private sealed record CommentMetadata(
        string? Description,
        IReadOnlyList<SandboxEnumOption> EnumOptions,
        decimal? Minimum,
        decimal? Maximum,
        string? DefaultValue);

    [GeneratedRegex(@"^\s*SandboxVars\s*=\s*\{\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex RootStartRegex();
    [GeneratedRegex(@"^\s+(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\{\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupStartRegex();
    [GeneratedRegex("^(?<indent>\\s+)(?<key>[A-Za-z_][A-Za-z0-9_]*)(?<separator>\\s*=\\s*)(?<value>true|false|[-+]?\\d+(?:\\.\\d+)?|\"(?:\\\\.|[^\"\\\\])*\")(?<suffix>\\s*,\\s*)$", RegexOptions.CultureInvariant)]
    private static partial Regex AssignmentRegex();
    [GeneratedRegex(@"^[-+]?\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerRegex();
    [GeneratedRegex(@"^[-+]?\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex DoubleRegex();
    [GeneratedRegex(@"^\s*(?<value>-?\d+)\s*=\s*(?<label>.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex EnumCommentRegex();
    [GeneratedRegex(@"Min:\s*(?<min>[-+]?\d+(?:\.\d+)?)\s+Max:\s*(?<max>[-+]?\d+(?:\.\d+)?)\s+Default:\s*(?<default>[-+]?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConstraintsRegex();
    [GeneratedRegex(@"Default\s*=\s*(?<default>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedDefaultRegex();
}
