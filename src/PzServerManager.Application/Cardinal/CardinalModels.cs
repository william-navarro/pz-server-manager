namespace PzServerManager.Application.Cardinal;

public enum CardinalMode
{
    Desligado,
    Observar,
    Ativo
}

public enum CardinalActionKind
{
    HordaDirecionada,
    HordaTematica,
    Som,
    Helicoptero,
    Alarme,
    TiroDistante,
    Tempestade,
    Neblina,
    RaidAlife,
    ScoutsAlife,
    ItemRaro,
    Esconderijo,
    Airdrop,
    VeiculoAbandonado,
    Chuva,
    Trovao,
    Raio,
    Mensagem
}

public enum CardinalDecisionSource
{
    Regra,
    Llm,
    Manual,
    Ponte
}

public enum CardinalEventStatus
{
    Observado,
    Enfileirado,
    Executado,
    Bloqueado,
    Falhou
}

public sealed record CardinalEventOptions
{
    public bool Enabled { get; set; } = true;
    public int Weight { get; set; } = 10;
    public int CooldownMinutes { get; set; } = 30;
}

public sealed record CardinalSpawnPoint
{
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Radius { get; set; } = 10;
    public string[] AllowedKits { get; set; } = [];
}

public sealed record CardinalSettings
{
    public CardinalMode Mode { get; set; } = CardinalMode.Desligado;
    public int PlayerCooldownMinutes { get; set; } = 30;
    public int MaximumEventsPerHour { get; set; } = 3;
    public int MaximumHordePerPlayer { get; set; } = 15;
    public int MaximumHordeTotal { get; set; } = 30;
    public int NewCharacterProtectionMinutes { get; set; } = 60;
    public int NewConnectionProtectionMinutes { get; set; } = 10;
    public int GroupDeathProtectionMinutes { get; set; } = 15;
    public int WakeProtectionMinutes { get; set; } = 5;
    public int NearbyZombieLimit { get; set; } = 10;
    public int GlobalHostileCooldownMinutes { get; set; } = 20;
    public int ReliefMinutes { get; set; } = 20;
    public bool ProtectSleeping { get; set; } = true;
    public bool ProtectResting { get; set; } = true;
    public bool ProtectWeakPlayers { get; set; } = true;
    public bool ProtectUnarmedPlayers { get; set; } = true;
    public bool ProtectMovingVehicles { get; set; } = true;
    public bool QuietHoursEnabled { get; set; }
    public string QuietHoursStart { get; set; } = "00:00";
    public string QuietHoursEnd { get; set; } = "08:00";
    public bool LlmEnabled { get; set; }
    public int LlmDailyCallBudget { get; set; } = 150;
    public int LlmIntervalMinutes { get; set; } = 10;
    public string[] RareItemAllowList { get; set; } =
        ["Base.FirstAidKit", "Base.CannedFood", "Base.556Box", "Base.9mmBox", "Base.Hammer"];
    public Dictionary<CardinalActionKind, CardinalEventOptions> Events { get; set; } = CreateDefaultEvents();
    public List<CardinalSpawnPoint> SpawnPoints { get; set; } = [];

    public CardinalSettings Validate()
    {
        if (PlayerCooldownMinutes is < 5 or > 1440 || GlobalHostileCooldownMinutes is < 5 or > 1440 || ReliefMinutes is < 5 or > 1440)
            throw new ArgumentException("Os intervalos da Cardinal devem ficar entre 5 e 1440 minutos.");
        if (MaximumEventsPerHour is < 1 or > 20)
            throw new ArgumentException("O teto deve ficar entre 1 e 20 eventos por hora.");
        if (MaximumHordePerPlayer is < 1 or > 50 || MaximumHordeTotal is < 1 or > 100 || MaximumHordeTotal < MaximumHordePerPlayer)
            throw new ArgumentException("Os limites de horda são inválidos.");
        if (NearbyZombieLimit is < 1 or > 100)
            throw new ArgumentException("O limite de zumbis próximos deve ficar entre 1 e 100.");
        if (NewCharacterProtectionMinutes is < 0 or > 1440 || NewConnectionProtectionMinutes is < 0 or > 120 ||
            GroupDeathProtectionMinutes is < 0 or > 120 || WakeProtectionMinutes is < 0 or > 60)
            throw new ArgumentException("Uma janela de proteção está fora do limite permitido.");
        if (!TimeOnly.TryParseExact(QuietHoursStart, "HH:mm", out _) || !TimeOnly.TryParseExact(QuietHoursEnd, "HH:mm", out _))
            throw new ArgumentException("O modo calmo deve usar horários HH:mm.");
        if (LlmDailyCallBudget is < 1 or > 1000 || LlmIntervalMinutes is < 10 or > 1440)
            throw new ArgumentException("Os limites do LLM são inválidos.");
        if (RareItemAllowList is null || RareItemAllowList.Length == 0 || RareItemAllowList.Length > 100 ||
            RareItemAllowList.Any(item => !CardinalValidation.IsItemType(item)))
            throw new ArgumentException("A lista de itens raros contém um tipo inválido.");
        Events ??= CreateDefaultEvents();
        foreach (var kind in Enum.GetValues<CardinalActionKind>())
        {
            if (!Events.TryGetValue(kind, out var options) || options is null) Events[kind] = new();
            else if (options.Weight is < 0 or > 100 || options.CooldownMinutes is < 1 or > 10080)
                throw new ArgumentException($"Peso ou intervalo inválido para {kind}.");
        }
        SpawnPoints ??= [];
        if (SpawnPoints.Count > 100 || SpawnPoints.Any(point => point is null || string.IsNullOrWhiteSpace(point.Name) || point.Name.Length > 80 ||
            point.Radius is < 1 or > 500 || point.AllowedKits is null || point.AllowedKits.Length > 20))
            throw new ArgumentException("Há um ponto fixo de suprimentos inválido.");
        return this;
    }

    private static Dictionary<CardinalActionKind, CardinalEventOptions> CreateDefaultEvents() =>
        Enum.GetValues<CardinalActionKind>().ToDictionary(kind => kind, kind => new CardinalEventOptions
        {
            Enabled = kind is not CardinalActionKind.VeiculoAbandonado,
            Weight = kind switch
            {
                CardinalActionKind.HordaDirecionada => 20,
                CardinalActionKind.Som or CardinalActionKind.TiroDistante or CardinalActionKind.Mensagem => 15,
                CardinalActionKind.RaidAlife or CardinalActionKind.Helicoptero => 4,
                _ => 8
            },
            CooldownMinutes = kind switch
            {
                CardinalActionKind.RaidAlife or CardinalActionKind.Helicoptero => 180,
                CardinalActionKind.HordaDirecionada or CardinalActionKind.HordaTematica => 60,
                _ => 30
            }
        });
}

public sealed record CardinalMoodles
{
    public int Hunger { get; init; }
    public int Thirst { get; init; }
    public int Pain { get; init; }
    public int Panic { get; init; }
    public int Tired { get; init; }
    public int Stress { get; init; }
    public int Drunk { get; init; }
    public int Sick { get; init; }
    public int Cold { get; init; }
    public int Hot { get; init; }
}

public sealed record CardinalPlayerSnapshot
{
    public string Username { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double HealthPercent { get; init; } = 100;
    public bool Infected { get; init; }
    public bool Bleeding { get; init; }
    public bool Fracture { get; init; }
    public bool Asleep { get; init; }
    public bool Sitting { get; init; }
    public bool Reading { get; init; }
    public bool InVehicle { get; init; }
    public double VehicleSpeed { get; init; }
    public bool InsideBuilding { get; init; }
    public bool AtBase { get; init; }
    public string? HandWeapon { get; init; }
    public bool HasMeleeWeapon { get; init; }
    public int WeaponAmmo { get; init; }
    public int InventoryAmmo { get; init; }
    public double CarryWeight { get; init; }
    public double MaximumCarryWeight { get; init; }
    public int Zombies20 { get; init; }
    public int Zombies30 { get; init; }
    public int Zombies50 { get; init; }
    public int Kills { get; init; }
    public double HoursSurvived { get; init; }
    public int GroupSize { get; init; } = 1;
    public DateTimeOffset? LastWakeAt { get; init; }
    public CardinalMoodles Moodles { get; init; } = new();
}

public sealed record CardinalWorldSnapshot
{
    public DateTimeOffset Timestamp { get; init; }
    public double WorldAgeHours { get; init; }
    public int Day { get; init; }
    public int Hour { get; init; }
    public int Minute { get; init; }
    public string Season { get; init; } = string.Empty;
    public bool Night { get; init; }
    public double LightLevel { get; init; }
    public double RainIntensity { get; init; }
    public double FogIntensity { get; init; }
    public string Weather { get; init; } = string.Empty;
    public IReadOnlyList<CardinalPlayerSnapshot> Players { get; init; } = [];
}

public enum PlayerLogEventKind { Connected, Disconnected, Position, CharacterCreated, Died, SurvivalHours, Noise }

public sealed record PlayerLogEvent(
    string Id,
    DateTimeOffset Timestamp,
    PlayerLogEventKind Kind,
    string Username,
    int? X = null,
    int? Y = null,
    int? Z = null,
    double? HoursSurvived = null);

public sealed record PlayerDeath(
    DateTimeOffset Timestamp,
    int? X,
    int? Y,
    int? Z,
    double HoursSurvived);

public sealed record PlayerActivitySummary
{
    public string Username { get; init; } = string.Empty;
    public bool Online { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
    public double CurrentHoursSurvived { get; init; }
    public double SurvivalRecordHours { get; init; }
    public int Deaths { get; init; }
    public int CharactersCreated { get; init; }
    public int Kills { get; init; }
    public double? HealthPercent { get; init; }
    public int ZombiesNearby { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }
    public int? Z { get; init; }
    public DateTimeOffset? LastNoiseAt { get; init; }
    public TimeSpan PlayedThisWeek { get; init; }
    public IReadOnlyList<PlayerDeath> RecentDeaths { get; init; } = [];
}

public sealed record CardinalTimelineEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; init; }
    public CardinalActionKind Action { get; init; }
    public string? Player { get; init; }
    public string Reason { get; init; } = string.Empty;
    public CardinalDecisionSource Source { get; init; }
    public CardinalEventStatus Status { get; init; }
    public string Result { get; init; } = string.Empty;
    public bool Hostile { get; init; }
    public bool ProtectionsOverridden { get; init; }
}

public sealed record CardinalUsage(int CallsToday, long InputTokensToday, long OutputTokensToday, DateOnly Day);

public sealed record CardinalDashboard(
    CardinalSettings Settings,
    CardinalWorldSnapshot? LatestSnapshot,
    IReadOnlyList<PlayerActivitySummary> Players,
    IReadOnlyList<CardinalTimelineEvent> Timeline,
    CardinalUsage Usage);

public sealed record CardinalActionRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public CardinalActionKind Kind { get; set; }
    public string? Player { get; set; }
    public int Amount { get; set; }
    public int Distance { get; set; } = 30;
    public string? ItemType { get; set; }
    public string? Message { get; set; }
    public bool OverrideProtections { get; set; }
    public string RequestedBy { get; set; } = "Cardinal";
    public Dictionary<string, string> Parameters { get; set; } = [];
}

public sealed record CardinalActionResult(CardinalEventStatus Status, string Message);

public sealed record CardinalLlmSuggestion(IReadOnlyList<CardinalActionRequest> Actions, string? Narrative, int InputTokens, int OutputTokens);

public static class CardinalActionCatalog
{
    public static bool IsHostile(CardinalActionKind kind) => kind is
        CardinalActionKind.HordaDirecionada or CardinalActionKind.HordaTematica or CardinalActionKind.Som or CardinalActionKind.Helicoptero or
        CardinalActionKind.Alarme or CardinalActionKind.TiroDistante or CardinalActionKind.Tempestade or
        CardinalActionKind.Neblina or CardinalActionKind.RaidAlife or CardinalActionKind.ScoutsAlife;

    public static bool NeedsPlayer(CardinalActionKind kind) => kind is not
        (CardinalActionKind.Chuva or CardinalActionKind.Trovao or CardinalActionKind.Raio or CardinalActionKind.Mensagem);

    public static string Label(CardinalActionKind kind) => kind switch
    {
        CardinalActionKind.HordaDirecionada => "Horda direcionada",
        CardinalActionKind.HordaTematica => "Horda temática",
        CardinalActionKind.Som => "Som atrator",
        CardinalActionKind.Helicoptero => "Helicóptero",
        CardinalActionKind.Alarme => "Alarme próximo",
        CardinalActionKind.TiroDistante => "Tiro distante",
        CardinalActionKind.Tempestade => "Tempestade",
        CardinalActionKind.Neblina => "Neblina",
        CardinalActionKind.RaidAlife => "Raid do Project A-Life",
        CardinalActionKind.ScoutsAlife => "Batedores do Project A-Life",
        CardinalActionKind.ItemRaro => "Item raro",
        CardinalActionKind.Esconderijo => "Esconderijo de suprimentos",
        CardinalActionKind.Airdrop => "Airdrop",
        CardinalActionKind.VeiculoAbandonado => "Veículo abandonado",
        CardinalActionKind.Chuva => "Chuva",
        CardinalActionKind.Trovao => "Trovão",
        CardinalActionKind.Raio => "Raio",
        _ => "Mensagem narrativa"
    };
}

public static class CardinalValidation
{
    public static bool IsItemType(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}
