using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Metrics;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.Metrics;

public sealed class JsonMetricsStore(IManagerPaths paths, TimeProvider timeProvider) : IMetricsStore
{
    private sealed record Document(List<MetricSample> Samples, List<MetricEvent> Events);
    private readonly string _path = Path.Combine(paths.DataRoot, "metrics-7d.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task AppendSampleAsync(MetricSample sample, CancellationToken cancellationToken = default) =>
        ChangeAsync(document => document.Samples.Add(sample), cancellationToken);

    public Task AppendEventAsync(MetricEvent metricEvent, CancellationToken cancellationToken = default) =>
        ChangeAsync(document => document.Events.Add(metricEvent), cancellationToken);

    public async Task<MetricsSnapshot> ReadAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken);
            return new(document.Samples.Where(item => item.Timestamp >= since).ToArray(),
                document.Events.Where(item => item.Timestamp >= since).ToArray());
        }
        finally { _gate.Release(); }
    }

    private async Task ChangeAsync(Action<Document> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadDocumentAsync(cancellationToken);
            change(document);
            var cutoff = timeProvider.GetUtcNow().AddDays(-7);
            document.Samples.RemoveAll(item => item.Timestamp < cutoff);
            document.Events.RemoveAll(item => item.Timestamp < cutoff);
            await AtomicJsonFile.WriteAsync(_path, document, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<Document> ReadDocumentAsync(CancellationToken cancellationToken) =>
        await AtomicJsonFile.ReadAsync<Document>(_path, cancellationToken) ?? new([], []);
}
