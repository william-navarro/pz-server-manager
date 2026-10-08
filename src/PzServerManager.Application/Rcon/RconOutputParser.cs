using System.Text.RegularExpressions;

namespace PzServerManager.Application.Rcon;

public static partial class RconOutputParser
{
    public static IReadOnlyList<string> ParsePlayers(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var header = PlayersHeaderPattern().Match(output);
        if (!header.Success)
        {
            throw new FormatException("A resposta do comando players não está no formato esperado.");
        }

        var players = output[(header.Index + header.Length)..]
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('-'))
            .Select(line => line[1..].Trim())
            .Where(name => name.Length > 0)
            .ToArray();
        var declaredCount = int.Parse(header.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (players.Length != declaredCount)
        {
            throw new FormatException($"O servidor informou {declaredCount} jogador(es), mas retornou {players.Length} nome(s).");
        }

        return players;
    }

    public static IReadOnlyList<string> ParseHelp(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => HelpLinePattern().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [GeneratedRegex(@"Players connected \((\d+)\):", RegexOptions.CultureInvariant)]
    private static partial Regex PlayersHeaderPattern();

    [GeneratedRegex(@"^\*\s+([A-Za-z0-9]+)\s+:", RegexOptions.CultureInvariant)]
    private static partial Regex HelpLinePattern();
}
