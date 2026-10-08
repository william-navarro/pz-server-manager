using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Cardinal;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.Cardinal;

public sealed class JsonCardinalStore(IManagerPaths paths, TimeProvider timeProvider) : ICardinalStore
{
    private sealed record PlaySession(DateTimeOffset StartedAt, DateTimeOffset EndedAt);
    private sealed record SeenEvent(string Id, DateTimeOffset Timestamp);
    private sealed class PlayerProfile
    {
        public string Username { get; set; } = string.Empty;
        public DateTimeOffset? LastSeen { get; set; }
        public DateTimeOffset? ConnectedAt { get; set; }
        public double CurrentHours { get; set; }
        public double RecordHours { get; set; }
        public int Deaths { get; set; }
        public int CharactersCreated { get; set; }
        public int Kills { get; set; }
        public double? HealthPercent { get; set; }
        public int ZombiesNearby { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Z { get; set; }
        public DateTimeOffset? LastNoiseAt { get; set; }
        public List<PlaySession> Sessions { get; set; } = [];
        public List<PlayerDeath> RecentDeaths { get; set; } = [];
    }
    private sealed class Document
    {
        public Dictionary<string, PlayerProfile> Profiles { get; set; } = [];
        public List<SeenEvent> SeenEvents { get; set; } = [];
        public List<CardinalWorldSnapshot> Snapshots { get; set; } = [];
        public List<CardinalTimelineEvent> Timeline { get; set; } = [];
        public CardinalUsage Usage { get; set; } = new(0, 0, 0, DateOnly.MinValue);
    }

    private readonly string _settingsPath = Path.Combine(paths.DataRoot, "cardinal", "settings.json");
    private readonly string _dataPath = Path.Combine(paths.DataRoot, "cardinal", "activity-7d.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<CardinalSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return (await AtomicJsonFile.ReadAsync<CardinalSettings>(_settingsPath, cancellationToken) ?? new()).Validate(); }
        finally { _gate.Release(); }
    }

    public async Task SaveSettingsAsync(CardinalSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        await _gate.WaitAsync(cancellationToken);
        try { await AtomicJsonFile.WriteAsync(_settingsPath, settings, cancellationToken); }
        finally { _gate.Release(); }
    }

    public Task ApplyLogEventsAsync(IReadOnlyList<PlayerLogEvent> events, CancellationToken cancellationToken = default) =>
        ChangeAsync(document =>
        {
            var seen = document.SeenEvents.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var item in events.OrderBy(item => item.Timestamp))
            {
                // Posição e barulho só valem pelo mais recente e chegam aos milhares (cmd.txt): não entram na lista de
                // deduplicação (que é regravada a cada ciclo); basta ignorar o que for mais antigo que o já conhecido.
                if (item.Kind is PlayerLogEventKind.Position or PlayerLogEventKind.Noise)
                {
                    var current = GetProfile(document, item.Username);
                    if (current.LastSeen is not null && item.Timestamp < current.LastSeen) continue;
                }
                else
                {
                    if (!seen.Add(item.Id)) continue;
                    document.SeenEvents.Add(new(item.Id, item.Timestamp));
                }
                var profile = GetProfile(document, item.Username);
                profile.LastSeen = profile.LastSeen is null || item.Timestamp > profile.LastSeen ? item.Timestamp : profile.LastSeen;
                if (item.X is not null) { profile.X = item.X; profile.Y = item.Y; profile.Z = item.Z; }
                switch (item.Kind)
                {
                    case PlayerLogEventKind.Connected:
                        profile.ConnectedAt ??= item.Timestamp;
                        break;
                    case PlayerLogEventKind.Disconnected:
                        CloseSession(profile, item.Timestamp);
                        break;
                    case PlayerLogEventKind.CharacterCreated:
                        profile.CharactersCreated++;
                        profile.CurrentHours = item.HoursSurvived ?? 0;
                        break;
                    case PlayerLogEventKind.SurvivalHours:
                        profile.CurrentHours = Math.Max(profile.CurrentHours, item.HoursSurvived ?? 0);
                        profile.RecordHours = Math.Max(profile.RecordHours, profile.CurrentHours);
                        break;
                    case PlayerLogEventKind.Died:
                        ApplyDeath(profile, item);
                        break;
                    case PlayerLogEventKind.Noise:
                        profile.LastNoiseAt = item.Timestamp;
                        break;
                }
            }
        }, cancellationToken);

    public Task UpdateSnapshotAsync(CardinalWorldSnapshot snapshot, CancellationToken cancellationToken = default) =>
        ChangeAsync(document =>
        {
            document.Snapshots.Add(snapshot);
            foreach (var item in snapshot.Players)
            {
                var profile = GetProfile(document, item.Username);
                profile.LastSeen = snapshot.Timestamp;
                profile.X = (int)Math.Round(item.X); profile.Y = (int)Math.Round(item.Y); profile.Z = (int)Math.Round(item.Z);
                profile.CurrentHours = item.HoursSurvived;
                profile.RecordHours = Math.Max(profile.RecordHours, item.HoursSurvived);
                profile.Kills = item.Kills;
                profile.HealthPercent = item.HealthPercent;
                profile.ZombiesNearby = item.Zombies30;
            }
        }, cancellationToken);

    public Task UpdateOnlinePlayersAsync(IReadOnlyCollection<string> players, DateTimeOffset timestamp, CancellationToken cancellationToken = default) =>
        ChangeAsync(document =>
        {
            var online = players.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var userName in online)
            {
                var profile = GetProfile(document, userName);
                profile.ConnectedAt ??= timestamp;
                profile.LastSeen = timestamp;
            }
            foreach (var profile in document.Profiles.Values.Where(profile => profile.ConnectedAt is not null && !online.Contains(profile.Username)))
                CloseSession(profile, timestamp);
        }, cancellationToken);

    public Task AppendTimelineAsync(CardinalTimelineEvent item, CancellationToken cancellationToken = default) =>
        ChangeAsync(document => document.Timeline.Add(item), cancellationToken);

    public Task RecordLlmUsageAsync(int inputTokens, int outputTokens, DateTimeOffset timestamp, CancellationToken cancellationToken = default) =>
        ChangeAsync(document =>
        {
            var day = DateOnly.FromDateTime(timestamp.LocalDateTime);
            document.Usage = document.Usage.Day == day
                ? document.Usage with { CallsToday = document.Usage.CallsToday + 1,
                    InputTokensToday = document.Usage.InputTokensToday + inputTokens,
                    OutputTokensToday = document.Usage.OutputTokensToday + outputTokens }
                : new(1, inputTokens, outputTokens, day);
        }, cancellationToken);

    public async Task<CardinalDashboard> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settings = (await AtomicJsonFile.ReadAsync<CardinalSettings>(_settingsPath, cancellationToken) ?? new()).Validate();
            var document = await ReadDocumentAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            var week = now.AddDays(-7);
            var players = document.Profiles.Values.Select(profile => new PlayerActivitySummary
            {
                Username = profile.Username,
                Online = profile.ConnectedAt is not null,
                LastSeen = profile.LastSeen,
                ConnectedAt = profile.ConnectedAt,
                CurrentHoursSurvived = profile.CurrentHours,
                SurvivalRecordHours = profile.RecordHours,
                Deaths = profile.Deaths,
                CharactersCreated = profile.CharactersCreated,
                Kills = profile.Kills,
                HealthPercent = profile.HealthPercent,
                ZombiesNearby = profile.ZombiesNearby,
                X = profile.X, Y = profile.Y, Z = profile.Z,
                LastNoiseAt = profile.LastNoiseAt,
                PlayedThisWeek = PlayedSince(profile, week, now),
                RecentDeaths = profile.RecentDeaths.OrderByDescending(item => item.Timestamp).ToArray()
            }).OrderByDescending(item => item.Online).ThenByDescending(item => item.LastSeen).ToArray();
            var usage = document.Usage.Day == DateOnly.FromDateTime(now.LocalDateTime)
                ? document.Usage : new CardinalUsage(0, 0, 0, DateOnly.FromDateTime(now.LocalDateTime));
            return new(settings, document.Snapshots.MaxBy(item => item.Timestamp), players,
                document.Timeline.OrderByDescending(item => item.Timestamp).Take(500).ToArray(), usage);
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
            document.SeenEvents.RemoveAll(item => item.Timestamp < cutoff);
            document.Snapshots.RemoveAll(item => item.Timestamp < cutoff);
            document.Timeline.RemoveAll(item => item.Timestamp < cutoff);
            foreach (var profile in document.Profiles.Values)
            {
                profile.Sessions.RemoveAll(item => item.EndedAt < cutoff);
                profile.RecentDeaths.RemoveAll(item => item.Timestamp < cutoff);
            }
            await AtomicJsonFile.WriteAsync(_dataPath, document, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<Document> ReadDocumentAsync(CancellationToken cancellationToken) =>
        await AtomicJsonFile.ReadAsync<Document>(_dataPath, cancellationToken) ?? new();

    private static PlayerProfile GetProfile(Document document, string userName)
    {
        var key = userName.Trim().ToUpperInvariant();
        if (!document.Profiles.TryGetValue(key, out var profile))
        {
            profile = new() { Username = userName.Trim() };
            document.Profiles[key] = profile;
        }
        return profile;
    }

    private static void ApplyDeath(PlayerProfile profile, PlayerLogEvent item)
    {
        var existingIndex = profile.RecentDeaths.FindIndex(death => Math.Abs((death.Timestamp - item.Timestamp).TotalSeconds) < 2 &&
            (item.X is null || death.X is null || death.X == item.X));
        var hours = item.HoursSurvived ?? profile.CurrentHours;
        if (existingIndex >= 0)
        {
            if (hours > profile.RecentDeaths[existingIndex].HoursSurvived)
                profile.RecentDeaths[existingIndex] = profile.RecentDeaths[existingIndex] with { HoursSurvived = hours };
            profile.RecordHours = Math.Max(profile.RecordHours, hours);
            return;
        }
        profile.Deaths++;
        profile.RecordHours = Math.Max(profile.RecordHours, hours);
        profile.CurrentHours = 0;
        profile.RecentDeaths.Add(new(item.Timestamp, item.X, item.Y, item.Z, hours));
    }

    private static void CloseSession(PlayerProfile profile, DateTimeOffset endedAt)
    {
        if (profile.ConnectedAt is not { } startedAt) return;
        if (endedAt > startedAt) profile.Sessions.Add(new(startedAt, endedAt));
        profile.ConnectedAt = null;
    }

    private static TimeSpan PlayedSince(PlayerProfile profile, DateTimeOffset since, DateTimeOffset now)
    {
        var ticks = profile.Sessions.Sum(item => Math.Max(0, (item.EndedAt - (item.StartedAt < since ? since : item.StartedAt)).Ticks));
        if (profile.ConnectedAt is { } connectedAt) ticks += Math.Max(0, (now - (connectedAt < since ? since : connectedAt)).Ticks);
        return TimeSpan.FromTicks(ticks);
    }
}
