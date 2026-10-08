using System.Text.Json;

namespace PzServerManager.Infrastructure.Configuration;

internal static class AtomicJsonFile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return default;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
