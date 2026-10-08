using System.Globalization;

namespace PzServerManager.Infrastructure.ManagedServer;

public sealed record SystemdShowData(
    string ActiveState,
    string SubState,
    int? MainProcessId,
    DateTimeOffset? ActiveEnterTimestamp,
    int RestartCount,
    long? MemoryCurrent,
    ulong? CpuUsageNanoseconds);

public static class SystemdShowParser
{
    public static SystemdShowData Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var values = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.Ordinal);

        values.TryGetValue("ActiveState", out var activeState);
        values.TryGetValue("SubState", out var subState);
        var pid = ParsePositiveInt(values.GetValueOrDefault("MainPID"));
        var restarts = int.TryParse(
            values.GetValueOrDefault("NRestarts"),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsedRestarts)
            ? parsedRestarts
            : 0;

        DateTimeOffset? timestamp = null;
        var rawTimestamp = values.GetValueOrDefault("ActiveEnterTimestamp");
        if (!string.IsNullOrWhiteSpace(rawTimestamp) &&
            DateTimeOffset.TryParse(rawTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedTimestamp))
        {
            timestamp = parsedTimestamp;
        }

        long? memory = long.TryParse(values.GetValueOrDefault("MemoryCurrent"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var parsedMemory) && parsedMemory >= 0 ? parsedMemory : null;
        ulong? cpu = ulong.TryParse(values.GetValueOrDefault("CPUUsageNSec"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var parsedCpu) ? parsedCpu : null;
        return new SystemdShowData(activeState ?? string.Empty, subState ?? string.Empty, pid, timestamp, restarts, memory, cpu);
    }

    private static int? ParsePositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;
}
