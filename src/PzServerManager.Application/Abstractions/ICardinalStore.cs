using PzServerManager.Application.Cardinal;

namespace PzServerManager.Application.Abstractions;

public interface ICardinalStore
{
    Task<CardinalSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(CardinalSettings settings, CancellationToken cancellationToken = default);
    Task ApplyLogEventsAsync(IReadOnlyList<PlayerLogEvent> events, CancellationToken cancellationToken = default);
    Task UpdateSnapshotAsync(CardinalWorldSnapshot snapshot, CancellationToken cancellationToken = default);
    Task UpdateOnlinePlayersAsync(IReadOnlyCollection<string> players, DateTimeOffset timestamp, CancellationToken cancellationToken = default);
    Task AppendTimelineAsync(CardinalTimelineEvent item, CancellationToken cancellationToken = default);
    Task RecordLlmUsageAsync(int inputTokens, int outputTokens, DateTimeOffset timestamp, CancellationToken cancellationToken = default);
    Task<CardinalDashboard> ReadAsync(CancellationToken cancellationToken = default);
}

public interface ICardinalActionExecutor
{
    Task<CardinalActionResult> ExecuteAsync(CardinalActionRequest request, CardinalSettings settings, CancellationToken cancellationToken = default);
}

public interface ICardinalManager
{
    Task<CardinalDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(CardinalSettings settings, CancellationToken cancellationToken = default);
    Task RunDirectorCycleAsync(CancellationToken cancellationToken = default);
    Task<CardinalActionResult> ExecuteManualAsync(CardinalActionRequest request, CancellationToken cancellationToken = default);
}

public interface ICardinalLlmClient
{
    Task<CardinalLlmSuggestion?> SuggestAsync(CardinalDashboard dashboard, CancellationToken cancellationToken = default);
}
