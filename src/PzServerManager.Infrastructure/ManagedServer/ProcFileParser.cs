using System.Globalization;

namespace PzServerManager.Infrastructure.ManagedServer;

public sealed record SystemMemoryInfo(long? AvailableBytes, long? TotalBytes);

public static class ProcFileParser
{
    public static long? ParseProcessResidentMemory(string statusText) =>
        ParseKilobyteValue(statusText, "VmRSS");

    public static SystemMemoryInfo ParseSystemMemory(string memInfoText) => new(
        ParseKilobyteValue(memInfoText, "MemAvailable"),
        ParseKilobyteValue(memInfoText, "MemTotal"));

    private static long? ParseKilobyteValue(string text, string key)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator < 0 || !line[..separator].Equals(key, StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line[(separator + 1)..].Split(
                ' ',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var kilobytes))
            {
                return checked(kilobytes * 1024);
            }
        }

        return null;
    }
}
