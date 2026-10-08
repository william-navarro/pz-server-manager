namespace PzServerManager.Application.Configuration;

public sealed class ServerIniDocument
{
    private readonly List<Line> _lines;
    private readonly string _originalText;
    private readonly string _newLine;

    private ServerIniDocument(List<Line> lines, string originalText, string newLine)
    {
        _lines = lines;
        _originalText = originalText;
        _newLine = newLine;
    }

    public IReadOnlyList<string> Keys
    {
        get
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var line in _lines)
            {
                if (TryGetEntry(line.Content, out var key, out _) && seen.Add(key))
                {
                    keys.Add(key);
                }
            }

            return keys;
        }
    }

    public IReadOnlyList<string> DuplicateKeys
    {
        get
        {
            var duplicates = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var added = new HashSet<string>(StringComparer.Ordinal);

            foreach (var line in _lines)
            {
                if (TryGetEntry(line.Content, out var key, out _) &&
                    !seen.Add(key) &&
                    added.Add(key))
                {
                    duplicates.Add(key);
                }
            }

            return duplicates;
        }
    }

    public bool HasChanges => !string.Equals(_originalText, ToText(), StringComparison.Ordinal);

    public static ServerIniDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<Line>();
        string? detectedNewLine = null;
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            var isCrLf = index > start && text[index - 1] == '\r';
            var contentLength = index - start - (isCrLf ? 1 : 0);
            var terminator = isCrLf ? "\r\n" : "\n";
            detectedNewLine ??= terminator;
            lines.Add(new Line(text.Substring(start, contentLength), terminator));
            start = index + 1;
        }

        if (start < text.Length)
        {
            lines.Add(new Line(text[start..], string.Empty));
        }

        return new ServerIniDocument(lines, text, detectedNewLine ?? Environment.NewLine);
    }

    public string ToText()
    {
        if (_lines.Count == 0)
        {
            return string.Empty;
        }

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

    public bool ContainsKey(string key)
    {
        ValidateKey(key);
        return _lines.Any(line =>
            TryGetEntry(line.Content, out var currentKey, out _) &&
            currentKey.Equals(key, StringComparison.Ordinal));
    }

    public string? Get(string key)
    {
        ValidateKey(key);

        for (var index = _lines.Count - 1; index >= 0; index--)
        {
            if (TryGetEntry(_lines[index].Content, out var currentKey, out var value) &&
                currentKey.Equals(key, StringComparison.Ordinal))
            {
                return value;
            }
        }

        return null;
    }

    public void Set(string key, string value)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);

        if (value.Contains('\r') || value.Contains('\n'))
        {
            throw new ArgumentException("O valor não pode conter quebra de linha.", nameof(value));
        }

        for (var index = _lines.Count - 1; index >= 0; index--)
        {
            if (TryGetEntry(_lines[index].Content, out var currentKey, out _) &&
                currentKey.Equals(key, StringComparison.Ordinal))
            {
                _lines[index].Content = $"{key}={value}";
                return;
            }
        }

        AppendLine($"{key}={value}");
    }

    public string? GetDescription(string key)
    {
        ValidateKey(key);

        for (var index = _lines.Count - 1; index >= 0; index--)
        {
            if (!TryGetEntry(_lines[index].Content, out var currentKey, out _) ||
                !currentKey.Equals(key, StringComparison.Ordinal))
            {
                continue;
            }

            var comments = new List<string>();
            for (var commentIndex = index - 1;
                 commentIndex >= 0 && _lines[commentIndex].Content.StartsWith('#');
                 commentIndex--)
            {
                var comment = _lines[commentIndex].Content[1..];
                if (comment.StartsWith(' '))
                {
                    comment = comment[1..];
                }

                comments.Add(comment);
            }

            if (comments.Count == 0)
            {
                return null;
            }

            comments.Reverse();
            return string.Join('\n', comments);
        }

        return null;
    }

    public IReadOnlyList<string> GetList(string key)
    {
        var value = Get(key);
        if (value is null)
        {
            return Array.Empty<string>();
        }

        return value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    public void SetList(string key, IEnumerable<string> items)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(items);

        var normalizedItems = new List<string>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                throw new ArgumentException("A lista não pode conter itens vazios.", nameof(items));
            }

            if (item.Contains(';'))
            {
                throw new ArgumentException("Os itens da lista não podem conter ponto e vírgula.", nameof(items));
            }

            normalizedItems.Add(item.Trim());
        }

        Set(key, string.Join(';', normalizedItems));
    }

    public static bool IsSensitive(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("Token", StringComparison.OrdinalIgnoreCase);
    }

    private void AppendLine(string content)
    {
        if (_lines.Count == 0)
        {
            _lines.Add(new Line(content, string.Empty));
            return;
        }

        var hadFinalNewLine = _lines[^1].Terminator.Length > 0;
        if (!hadFinalNewLine)
        {
            _lines[^1].Terminator = _newLine;
        }

        _lines.Add(new Line(content, hadFinalNewLine ? _newLine : string.Empty));
    }

    private static bool TryGetEntry(string line, out string key, out string value)
    {
        if (line.StartsWith('#'))
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex < 0)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = line[..separatorIndex];
        value = line[(separatorIndex + 1)..];
        return true;
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (key.Contains('=') || key.Contains('#') || key.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "A chave não pode conter '=', '#', espaços ou quebras de linha.",
                nameof(key));
        }
    }

    private sealed class Line(string content, string terminator)
    {
        public string Content { get; set; } = content;
        public string Terminator { get; set; } = terminator;
    }
}
