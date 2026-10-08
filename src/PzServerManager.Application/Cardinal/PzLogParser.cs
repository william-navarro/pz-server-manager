using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PzServerManager.Application.Cardinal;

public static partial class PzLogParser
{
    public static PlayerLogEvent? Parse(string logType, string line, string sourceId, TimeZoneInfo? timeZone = null)
    {
        if (!TryTimestamp(line, timeZone ?? TimeZoneInfo.Local, out var timestamp)) return null;
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceId}\n{line}")))[..24];

        if (logType.Equals("PerkLog", StringComparison.OrdinalIgnoreCase))
        {
            var match = PerkPattern().Match(line);
            if (!match.Success) return null;
            var action = match.Groups["action"].Value;
            var kind = action switch
            {
                "Died" => PlayerLogEventKind.Died,
                _ when action.StartsWith("Created Player", StringComparison.Ordinal) => PlayerLogEventKind.CharacterCreated,
                _ => PlayerLogEventKind.SurvivalHours
            };
            return new(id, timestamp, kind, match.Groups["user"].Value,
                ParseInt(match, "x"), ParseInt(match, "y"), ParseInt(match, "z"),
                double.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture));
        }

        if (logType.Equals("cmd", StringComparison.OrdinalIgnoreCase))
        {
            var match = CommandPattern().Match(line);
            if (!match.Success) return null;
            var command = match.Groups["command"].Value;
            var kind = command.Contains("shot", StringComparison.OrdinalIgnoreCase) ||
                command.Contains("weaponNoise", StringComparison.OrdinalIgnoreCase) || command.Contains("firearm", StringComparison.OrdinalIgnoreCase)
                ? PlayerLogEventKind.Noise : PlayerLogEventKind.Position;
            return new(id, timestamp, kind, match.Groups["user"].Value,
                ParseInt(match, "x"), ParseInt(match, "y"), ParseInt(match, "z"));
        }

        if (logType.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            var match = UserConnectionPattern().Match(line);
            if (match.Success)
                return new(id, timestamp, match.Groups["event"].Value == "fully connected" ? PlayerLogEventKind.Connected : PlayerLogEventKind.Disconnected,
                    match.Groups["user"].Value, ParseInt(match, "x"), ParseInt(match, "y"), ParseInt(match, "z"));
            match = UserDeathPattern().Match(line);
            if (match.Success)
                return new(id, timestamp, PlayerLogEventKind.Died, match.Groups["user"].Value,
                    ParseInt(match, "x"), ParseInt(match, "y"), ParseInt(match, "z"));
        }

        if (logType.Equals("connections", StringComparison.OrdinalIgnoreCase))
        {
            var match = ConnectionPattern().Match(line);
            if (match.Success && match.Groups["user"].Value != "null")
                return new(id, timestamp, match.Groups["event"].Value == "fully-connected" ? PlayerLogEventKind.Connected : PlayerLogEventKind.Disconnected,
                    match.Groups["user"].Value);
        }
        return null;
    }

    private static bool TryTimestamp(string line, TimeZoneInfo timeZone, out DateTimeOffset timestamp)
    {
        timestamp = default;
        var match = TimestampPattern().Match(line);
        if (!match.Success || !DateTime.TryParseExact(match.Groups["value"].Value, "dd-MM-yy HH:mm:ss.fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        timestamp = new DateTimeOffset(local, timeZone.GetUtcOffset(local));
        return true;
    }

    private static int ParseInt(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex("^\\[(?<value>\\d{2}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}\\.\\d{3})\\]", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    [GeneratedRegex("^\\[[^]]+\\] \\[[^]]+\\]\\[(?<user>[^]]+)\\]\\[(?<x>-?\\d+),(?<y>-?\\d+),(?<z>-?\\d+)\\]\\[(?<action>[^]]+)\\].*\\[Hours Survived: (?<hours>\\d+(?:\\.\\d+)?)\\]", RegexOptions.CultureInvariant)]
    private static partial Regex PerkPattern();

    [GeneratedRegex("^\\[[^]]+\\] \\d+ \"(?<user>[^\"]+)\" (?<command>.+?) @ (?<x>-?\\d+),(?<y>-?\\d+),(?<z>-?\\d+)\\.", RegexOptions.CultureInvariant)]
    private static partial Regex CommandPattern();

    [GeneratedRegex("^\\[[^]]+\\] \\d+ \"(?<user>[^\"]+)\" (?<event>fully connected|disconnected player) \\((?<x>-?\\d+),(?<y>-?\\d+),(?<z>-?\\d+)\\)\\.", RegexOptions.CultureInvariant)]
    private static partial Regex UserConnectionPattern();

    [GeneratedRegex("^\\[[^]]+\\] user (?<user>.+?) died at \\((?<x>-?\\d+),(?<y>-?\\d+),(?<z>-?\\d+)\\)", RegexOptions.CultureInvariant)]
    private static partial Regex UserDeathPattern();

    [GeneratedRegex("^\\[[^]]+\\] event=\"(?<event>fully-connected|disconnect)\".* username=\"(?<user>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionPattern();
}
