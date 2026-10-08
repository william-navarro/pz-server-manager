using System.Globalization;
using System.Text;

namespace PzServerManager.Infrastructure.Configuration;

internal static class AtomicTextFileStore
{
    private const int MaximumBackupCount = 10;
    internal static readonly UTF8Encoding Utf8WithoutBom = new(false, true);

    public static async Task WriteAsync(string path, string text, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(temporaryPath, text, Utf8WithoutBom, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullPath)) File.Copy(fullPath, CreateBackupPath(fullPath));
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
            RemoveOldBackups(fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string CreateBackupPath(string path)
    {
        var timestamp = DateTime.Now;
        while (true)
        {
            var backupPath = $"{path}.{timestamp.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.bak";
            if (!File.Exists(backupPath)) return backupPath;
            timestamp = timestamp.AddMilliseconds(1);
        }
    }

    private static void RemoveOldBackups(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var backups = Directory.GetFiles(directory, $"{Path.GetFileName(path)}.*.bak")
            .OrderByDescending(backup => Path.GetFileName(backup), StringComparer.Ordinal)
            .ToArray();
        foreach (var backup in backups.Skip(MaximumBackupCount)) File.Delete(backup);
    }
}
