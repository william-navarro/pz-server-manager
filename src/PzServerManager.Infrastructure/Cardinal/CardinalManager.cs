using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Cardinal;

namespace PzServerManager.Infrastructure.Cardinal;

public sealed class CardinalManager(
    ICardinalStore store,
    ICardinalActionExecutor executor,
    ICardinalLlmClient llm,
    TimeProvider timeProvider) : ICardinalManager
{
    private DateTimeOffset? _lastLlmCall;
    public Task<CardinalDashboard> GetDashboardAsync(CancellationToken cancellationToken = default) => store.ReadAsync(cancellationToken);
    public Task SaveSettingsAsync(CardinalSettings settings, CancellationToken cancellationToken = default) => store.SaveSettingsAsync(settings, cancellationToken);

    public async Task RunDirectorCycleAsync(CancellationToken cancellationToken = default)
    {
        var dashboard = await store.ReadAsync(cancellationToken);
        if (dashboard.Settings.Mode == CardinalMode.Desligado || dashboard.LatestSnapshot is not { } snapshot || snapshot.Players.Count == 0) return;
        var now = timeProvider.GetUtcNow();
        if (dashboard.Timeline.Count(item => now - item.Timestamp < TimeSpan.FromHours(1) && item.Status != CardinalEventStatus.Bloqueado) >= dashboard.Settings.MaximumEventsPerHour) return;

        foreach (var player in snapshot.Players.OrderBy(item => item.Username, StringComparer.OrdinalIgnoreCase))
        {
            var lastForPlayer = dashboard.Timeline.FirstOrDefault(item => item.Player?.Equals(player.Username, StringComparison.OrdinalIgnoreCase) == true);
            if (lastForPlayer is not null && now - lastForPlayer.Timestamp < TimeSpan.FromMinutes(dashboard.Settings.PlayerCooldownMinutes)) continue;
            var request = ChooseAction(dashboard, player, now);
            if (request is null) continue;
            await DecideAndExecuteAsync(request, CardinalDecisionSource.Regra, "ciclo de tensão e alívio", dashboard, cancellationToken);
            break;
        }
        await RunLlmAsync(dashboard, now, cancellationToken);
    }

    private async Task RunLlmAsync(CardinalDashboard dashboard, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = dashboard.Settings;
        if (!settings.LlmEnabled || dashboard.LatestSnapshot?.Players.Count is null or 0 ||
            dashboard.Usage.CallsToday >= settings.LlmDailyCallBudget ||
            (_lastLlmCall is not null && now - _lastLlmCall < TimeSpan.FromMinutes(settings.LlmIntervalMinutes))) return;
        _lastLlmCall = now;
        var suggestion = await llm.SuggestAsync(dashboard, cancellationToken);
        if (suggestion is null) return;
        await store.RecordLlmUsageAsync(suggestion.InputTokens, suggestion.OutputTokens, now, cancellationToken);
        foreach (var request in suggestion.Actions.Take(2))
        {
            var refreshed = await store.ReadAsync(cancellationToken);
            if (refreshed.Timeline.Count(item => now - item.Timestamp < TimeSpan.FromHours(1) && item.Status != CardinalEventStatus.Bloqueado) >= settings.MaximumEventsPerHour) break;
            await DecideAndExecuteAsync(request, CardinalDecisionSource.Llm, "sugestão do Claude Haiku validada pela política local", refreshed, cancellationToken);
        }
        if (!string.IsNullOrWhiteSpace(suggestion.Narrative))
        {
            var refreshed = await store.ReadAsync(cancellationToken);
            if (refreshed.Timeline.Count(item => now - item.Timestamp < TimeSpan.FromHours(1) && item.Status != CardinalEventStatus.Bloqueado) < settings.MaximumEventsPerHour)
                await DecideAndExecuteAsync(new CardinalActionRequest { Kind = CardinalActionKind.Mensagem, Message = suggestion.Narrative,
                    RequestedBy = "Cardinal LLM", Amount = 0, Distance = 30 }, CardinalDecisionSource.Llm,
                    "narração opcional do Claude Haiku", refreshed, cancellationToken);
        }
    }

    public async Task<CardinalActionResult> ExecuteManualAsync(CardinalActionRequest request, CancellationToken cancellationToken = default)
    {
        var dashboard = await store.ReadAsync(cancellationToken);
        request = request with { Id = Guid.NewGuid().ToString("N"), RequestedBy = string.IsNullOrWhiteSpace(request.RequestedBy) ? "admin" : request.RequestedBy };
        return await DecideAndExecuteAsync(request, CardinalDecisionSource.Manual, "teste manual do administrador", dashboard, cancellationToken);
    }

    private async Task<CardinalActionResult> DecideAndExecuteAsync(CardinalActionRequest request, CardinalDecisionSource source,
        string reason, CardinalDashboard dashboard, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        try { CardinalRequestValidator.Validate(request, dashboard.Settings); }
        catch (ArgumentException exception)
        {
            return await RecordAsync(request, source, reason, CardinalEventStatus.Bloqueado, exception.Message, dashboard, cancellationToken);
        }
        if (dashboard.Settings.Mode == CardinalMode.Desligado)
            return await RecordAsync(request, source, reason, CardinalEventStatus.Bloqueado, "A Cardinal está desligada.", dashboard, cancellationToken);
        var player = dashboard.LatestSnapshot?.Players.FirstOrDefault(item => item.Username.Equals(request.Player, StringComparison.OrdinalIgnoreCase));
        if (CardinalActionCatalog.IsHostile(request.Kind) && player is null)
            return await RecordAsync(request, source, reason, CardinalEventStatus.Bloqueado, "Não há snapshot atual do jogador.", dashboard, cancellationToken);
        if (player is not null && !request.OverrideProtections)
        {
            var blocks = CardinalPolicy.GetBlocks(dashboard.Settings, dashboard, player, request.Kind, now);
            if (blocks.Count > 0)
                return await RecordAsync(request, source, reason, CardinalEventStatus.Bloqueado, string.Join("; ", blocks), dashboard, cancellationToken);
        }
        if (!dashboard.Settings.Events.TryGetValue(request.Kind, out var options) || (!options.Enabled && source != CardinalDecisionSource.Manual))
            return await RecordAsync(request, source, reason, CardinalEventStatus.Bloqueado, "Esse evento está desativado.", dashboard, cancellationToken);
        var previous = dashboard.Timeline.FirstOrDefault(item => item.Action == request.Kind);
        if (source != CardinalDecisionSource.Manual && previous is not null && now - previous.Timestamp < TimeSpan.FromMinutes(options.CooldownMinutes)) return new(CardinalEventStatus.Bloqueado, "Evento em intervalo próprio.");
        if (dashboard.Settings.Mode == CardinalMode.Observar)
            return await RecordAsync(request, source, reason, CardinalEventStatus.Observado, $"Faria: {CardinalActionCatalog.Label(request.Kind)}.", dashboard, cancellationToken);
        var result = await executor.ExecuteAsync(request, dashboard.Settings, cancellationToken);
        return await RecordAsync(request, source, reason, result.Status, result.Message, dashboard, cancellationToken);
    }

    private async Task<CardinalActionResult> RecordAsync(CardinalActionRequest request, CardinalDecisionSource source, string reason,
        CardinalEventStatus status, string result, CardinalDashboard dashboard, CancellationToken cancellationToken)
    {
        var item = new CardinalTimelineEvent
        {
            Id = request.Id,
            Timestamp = timeProvider.GetUtcNow(),
            Action = request.Kind,
            Player = request.Player,
            Reason = reason,
            Source = source,
            Status = status,
            Result = result,
            Hostile = CardinalActionCatalog.IsHostile(request.Kind),
            ProtectionsOverridden = request.OverrideProtections
        };
        await store.AppendTimelineAsync(item, cancellationToken);
        return new(status, result);
    }

    private static CardinalActionRequest? ChooseAction(CardinalDashboard dashboard, CardinalPlayerSnapshot player, DateTimeOffset now)
    {
        if (CardinalPolicy.IsWeak(player))
        {
            if (dashboard.Settings.Events[CardinalActionKind.Esconderijo].Enabled && dashboard.Settings.SpawnPoints.Count > 0)
            {
                var point = dashboard.Settings.SpawnPoints[(now.Minute + player.Username.Length) % dashboard.Settings.SpawnPoints.Count];
                return new() { Kind = CardinalActionKind.Esconderijo, Player = player.Username, Amount = 1, Distance = 100,
                    Parameters = new() { ["x"] = point.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["y"] = point.Y.ToString(System.Globalization.CultureInfo.InvariantCulture), ["z"] = point.Z.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["radius"] = point.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture), ["kits"] = string.Join(',', point.AllowedKits) } };
            }
            if (!dashboard.Settings.Events[CardinalActionKind.ItemRaro].Enabled) return null;
            return new() { Kind = CardinalActionKind.ItemRaro, Player = player.Username, Amount = 1, Distance = 30, ItemType = dashboard.Settings.RareItemAllowList[0] };
        }
        var tension = CardinalPolicy.CalculateTension(player,
            dashboard.Timeline.FirstOrDefault(item => item.Player?.Equals(player.Username, StringComparison.OrdinalIgnoreCase) == true), now);
        if (tension >= 35 || player.HoursSurvived < 1) return null;
        CardinalActionKind[] candidates = [CardinalActionKind.Som, CardinalActionKind.TiroDistante,
            CardinalActionKind.HordaDirecionada, CardinalActionKind.HordaTematica, CardinalActionKind.Alarme,
            CardinalActionKind.ScoutsAlife];
        var enabled = candidates.Where(kind => dashboard.Settings.Events[kind].Enabled && dashboard.Settings.Events[kind].Weight > 0).ToArray();
        if (enabled.Length == 0) return null;
        var total = enabled.Sum(kind => dashboard.Settings.Events[kind].Weight);
        var seed = HashCode.Combine(player.Username.ToUpperInvariant(), now.UtcDateTime.Date, now.Hour);
        var pick = (seed & int.MaxValue) % total;
        var selected = enabled[0];
        foreach (var kind in enabled)
        {
            pick -= dashboard.Settings.Events[kind].Weight;
            if (pick < 0) { selected = kind; break; }
        }
        var group = Math.Max(1, player.GroupSize);
        var amount = Math.Min(dashboard.Settings.MaximumHordeTotal, dashboard.Settings.MaximumHordePerPlayer * group);
        return new() { Kind = selected, Player = player.Username,
            Amount = selected is CardinalActionKind.HordaDirecionada or CardinalActionKind.HordaTematica ? amount : 1,
            Distance = 30 };
    }
}
