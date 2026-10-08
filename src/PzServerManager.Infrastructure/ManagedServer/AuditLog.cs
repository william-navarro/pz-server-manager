using System.Text;
using PzServerManager.Application.Abstractions;

namespace PzServerManager.Infrastructure.ManagedServer;

public sealed class AuditLog(IManagerPaths paths) : IAuditLog
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task WriteAsync(
        string userName,
        string action,
        string result,
        CancellationToken cancellationToken = default)
    {
        var safeUser = Sanitize(userName);
        var safeResult = Sanitize(result);
        var line = $"{DateTimeOffset.Now:O}\t{safeUser}\t{action}\t{safeResult}{Environment.NewLine}";

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(paths.DataRoot);
            await File.AppendAllTextAsync(
                Path.Combine(paths.DataRoot, "audit.log"),
                line,
                new UTF8Encoding(false),
                cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
}
