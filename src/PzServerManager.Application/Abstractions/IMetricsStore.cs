using PzServerManager.Application.Metrics;

namespace PzServerManager.Application.Abstractions;

public interface IMetricsStore
{
    Task AppendSampleAsync(MetricSample sample, CancellationToken cancellationToken = default);
    Task AppendEventAsync(MetricEvent metricEvent, CancellationToken cancellationToken = default);
    Task<MetricsSnapshot> ReadAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
}
