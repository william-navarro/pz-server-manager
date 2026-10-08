using PzServerManager.Application.Rcon;

namespace PzServerManager.Application.Cardinal;

public static class CardinalPolicy
{
    public static IReadOnlyList<string> GetBlocks(
        CardinalSettings settings,
        CardinalDashboard dashboard,
        CardinalPlayerSnapshot player,
        CardinalActionKind action,
        DateTimeOffset now)
    {
        if (!CardinalActionCatalog.IsHostile(action)) return [];
        var blocks = new List<string>();
        var activity = dashboard.Players.FirstOrDefault(item => item.Username.Equals(player.Username, StringComparison.OrdinalIgnoreCase));
        if (settings.ProtectSleeping && player.Asleep) blocks.Add("jogador dormindo");
        if (settings.ProtectResting && (player.Sitting || player.Reading)) blocks.Add("jogador sentado, lendo ou descansando");
        if (player.LastWakeAt is { } wake && now - wake < TimeSpan.FromMinutes(settings.WakeProtectionMinutes)) blocks.Add("jogador acabou de acordar");
        if (settings.ProtectWeakPlayers && IsWeak(player)) blocks.Add("jogador fraco, ferido ou doente");
        if (player.Zombies30 >= settings.NearbyZombieLimit) blocks.Add("já há muitos zumbis próximos");
        if (settings.ProtectUnarmedPlayers && player.WeaponAmmo + player.InventoryAmmo == 0 && !player.HasMeleeWeapon) blocks.Add("jogador sem munição e sem arma corpo a corpo");
        if (settings.ProtectMovingVehicles && player.InVehicle && Math.Abs(player.VehicleSpeed) > 1) blocks.Add("jogador em veículo em movimento");
        if (player.HoursSurvived * 60 < settings.NewCharacterProtectionMinutes) blocks.Add("personagem novo");
        if (activity?.ConnectedAt is { } connectedAt && now - connectedAt < TimeSpan.FromMinutes(settings.NewConnectionProtectionMinutes)) blocks.Add("entrada recente no servidor");
        var groupNames = (dashboard.LatestSnapshot?.Players ?? [])
            .Where(member => Distance(member, player) <= 30)
            .Select(member => member.Username)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groupDeaths = dashboard.Players.Where(member => groupNames.Contains(member.Username) && member.RecentDeaths.Any(death =>
            now - death.Timestamp < TimeSpan.FromMinutes(settings.GroupDeathProtectionMinutes)));
        if (groupDeaths.Any()) blocks.Add("morte recente no grupo");
        var lastHostile = dashboard.Timeline.FirstOrDefault(item => item.Hostile && item.Status is CardinalEventStatus.Executado or CardinalEventStatus.Enfileirado);
        if (lastHostile is not null && now - lastHostile.Timestamp < TimeSpan.FromMinutes(settings.GlobalHostileCooldownMinutes)) blocks.Add("teto global de eventos hostis");
        if (lastHostile is not null && now - lastHostile.Timestamp < TimeSpan.FromMinutes(settings.ReliefMinutes)) blocks.Add("janela obrigatória de alívio");
        if (IsQuietTime(settings, now)) blocks.Add("modo calmo agendado");
        return blocks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static int CalculateTension(CardinalPlayerSnapshot player, CardinalTimelineEvent? lastEvent, DateTimeOffset now)
    {
        var nearby = Math.Min(45, player.Zombies30 * 4);
        var physical = Math.Min(35, (int)Math.Round(100 - player.HealthPercent) / 2 + player.Moodles.Panic * 4 + player.Moodles.Pain * 4);
        var recent = lastEvent is null ? 0 : Math.Max(0, 20 - (int)(now - lastEvent.Timestamp).TotalMinutes);
        return Math.Clamp(nearby + physical + recent, 0, 100);
    }

    public static bool IsWeak(CardinalPlayerSnapshot player) =>
        player.HealthPercent < 60 || player.Bleeding || player.Fracture || player.Infected ||
        player.Moodles.Pain >= 3 || player.Moodles.Tired >= 3 || player.Moodles.Hunger >= 3 ||
        player.Moodles.Thirst >= 3 || player.Moodles.Sick >= 3 || player.Moodles.Drunk > 0;

    public static bool IsQuietTime(CardinalSettings settings, DateTimeOffset now)
    {
        if (!settings.QuietHoursEnabled || !TimeOnly.TryParseExact(settings.QuietHoursStart, "HH:mm", out var start) ||
            !TimeOnly.TryParseExact(settings.QuietHoursEnd, "HH:mm", out var end)) return false;
        var current = TimeOnly.FromDateTime(now.LocalDateTime);
        return start <= end ? current >= start && current < end : current >= start || current < end;
    }

    private static double Distance(CardinalPlayerSnapshot left, CardinalPlayerSnapshot right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return Math.Sqrt(x * x + y * y);
    }
}

public static class CardinalRequestValidator
{
    public static void Validate(CardinalActionRequest request, CardinalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        if (CardinalActionCatalog.NeedsPlayer(request.Kind)) RconArgumentValidator.UserName(request.Player ?? string.Empty);
        if (request.Amount is < 0 or > 100) throw new ArgumentException("A quantidade deve ficar entre 0 e 100.");
        if (request.Distance is < 25 or > 500) throw new ArgumentException("A distância deve ficar entre 25 e 500 tiles.");
        if (request.Kind is CardinalActionKind.HordaDirecionada or CardinalActionKind.HordaTematica &&
            (request.Amount < 1 || request.Amount > settings.MaximumHordeTotal))
            throw new ArgumentException("O tamanho da horda ultrapassa o teto configurado.");
        if (request.Kind == CardinalActionKind.ItemRaro &&
            (request.ItemType is null || !settings.RareItemAllowList.Contains(request.ItemType, StringComparer.Ordinal)))
            throw new ArgumentException("O item não está na lista branca da Cardinal.");
        if (request.Message is not null) RconArgumentValidator.Text(request.Message, "mensagem");
    }
}
