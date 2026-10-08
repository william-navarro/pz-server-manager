namespace PzServerManager.Application.Metrics;

public sealed record MetricSample(DateTimeOffset Timestamp, long? MemoryBytes, double? CpuPercent, int? PlayersOnline);
public sealed record MetricEvent(DateTimeOffset Timestamp, string Kind, string Label);
public sealed record MetricsSnapshot(IReadOnlyList<MetricSample> Samples, IReadOnlyList<MetricEvent> Events);
