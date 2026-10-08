using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Cardinal;
using PzServerManager.Application.Rcon;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.Cardinal;

public sealed class CardinalLogCollector(
    IOptions<ManagerOptions> options,
    IManagerPaths paths,
    ICardinalStore store,
    ServerAdministrationService administration,
    TimeProvider timeProvider,
    ILogger<CardinalLogCollector> logger)
{
    private sealed record OffsetState(Dictionary<string, long> Files);
    private readonly string _logsPath = Path.Combine(options.Value.ManagedServer!.DataPath, "Logs");
    private readonly string _snapshotPath = Path.Combine(options.Value.ManagedServer!.DataPath, "Lua", "cardinal", "estado.txt");
    private readonly string _offsetPath = Path.Combine(paths.DataRoot, "cardinal", "log-offsets.json");
    private DateTimeOffset? _lastSnapshotTimestamp;

    public async Task CollectAsync(CancellationToken cancellationToken = default)
    {
        await CollectLogsAsync(cancellationToken);
        await CollectSnapshotAsync(cancellationToken);
        try
        {
            var players = await administration.GetPlayersAsync(cancellationToken);
            await store.UpdateOnlinePlayersAsync(players, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (Exception exception) { logger.LogDebug(exception, "RCON indisponível durante coleta da Cardinal"); }
    }

    private async Task CollectLogsAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_logsPath)) return;
        var offsets = await AtomicJsonFile.ReadAsync<OffsetState>(_offsetPath, cancellationToken) ?? new([]);
        var supported = new HashSet<string>(["PerkLog", "user", "cmd"], StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(_logsPath, "*.txt", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => supported.Contains(GetLogType(file.Name)))
            .GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(file => file.LastWriteTimeUtc).First())
            .OrderBy(file => GetLogType(file.Name).Equals("PerkLog", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToArray();
        var collected = new List<PlayerLogEvent>();
        foreach (var file in files)
        {
            offsets.Files.TryGetValue(file.Name, out var offset);
            if (file.Length < offset) offset = 0;
            if (file.Length == offset) continue;
            await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var lineNumber = 0;
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var item = PzLogParser.Parse(GetLogType(file.Name), line, $"{file.Name}:{offset}:{lineNumber++}", timeProvider.LocalTimeZone);
                if (item is not null) collected.Add(item);
            }
            offsets.Files[file.Name] = file.Length;
        }
        if (collected.Count > 0) await store.ApplyLogEventsAsync(collected, cancellationToken);
        await AtomicJsonFile.WriteAsync(_offsetPath, offsets, cancellationToken);
    }

    private async Task CollectSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_snapshotPath)) return;
        try
        {
            await using var stream = new FileStream(_snapshotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var snapshot = await JsonSerializer.DeserializeAsync<CardinalWorldSnapshot>(stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
            if (snapshot is null || snapshot.Timestamp == default || snapshot.Timestamp == _lastSnapshotTimestamp) return;
            _lastSnapshotTimestamp = snapshot.Timestamp;
            await store.UpdateSnapshotAsync(snapshot, cancellationToken);
        }
        catch (JsonException exception) { logger.LogDebug(exception, "Snapshot da ponte Cardinal estava sendo substituído"); }
        catch (IOException exception) { logger.LogDebug(exception, "Snapshot da ponte Cardinal indisponível durante a leitura"); }
    }

    private static string GetLogType(string fileName)
    {
        var underscore = fileName.LastIndexOf('_');
        return underscore < 0 ? Path.GetFileNameWithoutExtension(fileName) : fileName[(underscore + 1)..^4];
    }
}
