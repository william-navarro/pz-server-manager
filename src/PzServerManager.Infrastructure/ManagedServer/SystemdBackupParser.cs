using System.Globalization;
using PzServerManager.Application.Backups;

namespace PzServerManager.Infrastructure.ManagedServer;

public static class SystemdBackupParser
{
    public static DateTimeOffset? ParseNextElapse(string output)
    {
        var values = ParseProperties(output);
        return ParseTimestamp(values.GetValueOrDefault("NextElapseUSecRealtime"));
    }

    public static IReadOnlyList<BackupServiceResult> ParseServiceResults(
        string output,
        IReadOnlyList<string> unitNames)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(unitNames);
        var blocks = output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.None)
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToArray();

        var results = new List<BackupServiceResult>(unitNames.Count);
        for (var index = 0; index < unitNames.Count; index++)
        {
            var values = index < blocks.Length
                ? ParseProperties(blocks[index])
                : new Dictionary<string, string>(StringComparer.Ordinal);
            results.Add(new BackupServiceResult(
                unitNames[index],
                values.GetValueOrDefault("Result") ?? string.Empty,
                ParseTimestamp(values.GetValueOrDefault("ExecMainExitTimestamp"))));
        }

        return results;
    }

    public static (string ActiveState, string Result) ParseOperationState(string output)
    {
        var values = ParseProperties(output);
        return (
            values.GetValueOrDefault("ActiveState") ?? string.Empty,
            values.GetValueOrDefault("Result") ?? string.Empty);
    }

    private static Dictionary<string, string> ParseProperties(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            values[line[..separator]] = line[(separator + 1)..].Trim();
        }

        return values;
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("n/a", StringComparison.OrdinalIgnoreCase)) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var timestamp))
        {
            return timestamp;
        }

        var firstSpace = value.IndexOf(' ');
        return firstSpace >= 0 && DateTimeOffset.TryParse(
            value[(firstSpace + 1)..],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out timestamp)
            ? timestamp
            : null;
    }
}
