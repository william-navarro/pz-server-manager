using System.Globalization;
using System.Text.RegularExpressions;

namespace PzServerManager.Application.Backups;

public sealed class BackupFileNameParser
{
    private readonly Regex _fileNamePattern;

    public BackupFileNameParser(string serverName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        _fileNamePattern = new Regex(
            $@"^{Regex.Escape(serverName)}-([0-9]{{8}})-([0-9]{{6}})-([a-z0-9-]{{1,20}})\.tar\.gz$",
            RegexOptions.CultureInvariant);
    }

    public bool TryParse(string fileName, long sizeBytes, out BackupFile? backup)
    {
        backup = null;
        if (string.IsNullOrWhiteSpace(fileName) || sizeBytes < 0)
        {
            return false;
        }

        var match = _fileNamePattern.Match(fileName);
        if (!match.Success || !DateTimeOffset.TryParseExact(
                $"{match.Groups[1].Value}-{match.Groups[2].Value}",
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var timestamp) ||
            !TryParseReason(match.Groups[3].Value, out var reason))
        {
            return false;
        }

        backup = new BackupFile(fileName, timestamp, reason, sizeBytes);
        return true;
    }

    public IReadOnlyList<BackupFile> ParseAndSort(IEnumerable<(string FileName, long SizeBytes)> files) =>
        files.Select(file => TryParse(file.FileName, file.SizeBytes, out var parsed) ? parsed : null)
            .Where(file => file is not null)
            .OrderByDescending(file => file!.Timestamp)
            .ThenBy(file => file!.FileName, StringComparer.Ordinal)
            .Cast<BackupFile>()
            .ToArray();

    private static bool TryParseReason(string value, out BackupReason reason)
    {
        reason = value switch
        {
            "manual" => BackupReason.Manual,
            "diario" => BackupReason.Daily,
            "antes-restaurar" => BackupReason.BeforeRestore,
            "antes-zerar" => BackupReason.BeforeReset,
            "atualizacao-mods" => BackupReason.UpdateMods,
            "atualizacao-jogo" => BackupReason.UpdateGame,
            _ => default
        };
        return value is "manual" or "diario" or "antes-restaurar" or "antes-zerar" or "atualizacao-mods" or "atualizacao-jogo";
    }
}
