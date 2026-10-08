namespace PzServerManager.Application.ConfigurationHistory;

public enum ConfigurationFileKind { ServerIni, SandboxVars }
public enum DiffLineKind { Unchanged, Added, Removed }

public sealed record ConfigurationBackup(
    string FileName,
    ConfigurationFileKind Kind,
    DateTimeOffset LastModified,
    long SizeBytes);

public sealed record DiffLine(DiffLineKind Kind, int? OldLineNumber, int? NewLineNumber, string Text);

public sealed record ConfigurationComparison(
    ConfigurationBackup Backup,
    string CurrentFileName,
    IReadOnlyList<DiffLine> Lines);

public static class LineDiff
{
    public static IReadOnlyList<DiffLine> Compare(string oldText, string newText)
    {
        ArgumentNullException.ThrowIfNull(oldText);
        ArgumentNullException.ThrowIfNull(newText);
        var oldLines = Split(oldText);
        var newLines = Split(newText);
        var lengths = new int[oldLines.Length + 1, newLines.Length + 1];
        for (var oldIndex = oldLines.Length - 1; oldIndex >= 0; oldIndex--)
        for (var newIndex = newLines.Length - 1; newIndex >= 0; newIndex--)
            lengths[oldIndex, newIndex] = oldLines[oldIndex] == newLines[newIndex]
                ? lengths[oldIndex + 1, newIndex + 1] + 1
                : Math.Max(lengths[oldIndex + 1, newIndex], lengths[oldIndex, newIndex + 1]);

        var result = new List<DiffLine>();
        var i = 0; var j = 0;
        while (i < oldLines.Length || j < newLines.Length)
        {
            if (i < oldLines.Length && j < newLines.Length && oldLines[i] == newLines[j])
            {
                result.Add(new(DiffLineKind.Unchanged, i + 1, j + 1, oldLines[i])); i++; j++;
            }
            else if (j < newLines.Length && (i == oldLines.Length || lengths[i, j + 1] >= lengths[i + 1, j]))
            {
                result.Add(new(DiffLineKind.Added, null, j + 1, newLines[j++]));
            }
            else
            {
                result.Add(new(DiffLineKind.Removed, i + 1, null, oldLines[i++]));
            }
        }
        return result;
    }

    private static string[] Split(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}
