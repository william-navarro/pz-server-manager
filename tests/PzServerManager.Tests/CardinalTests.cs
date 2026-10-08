using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Cardinal;
using PzServerManager.Application.Rcon;
using PzServerManager.Infrastructure.Cardinal;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Tests;

public sealed class CardinalTests
{
    private static readonly TimeZoneInfo FixtureZone = TimeZoneInfo.CreateCustomTimeZone("fixture", TimeSpan.FromHours(-3), "fixture", "fixture");

    [Fact]
    public void LogParser_ReadsAnonymizedRealFormats()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cardinal");
        var perk = File.ReadAllLines(Path.Combine(root, "2026-10-03_16-19_PerkLog.txt"));
        var user = File.ReadAllLines(Path.Combine(root, "2026-10-03_16-19_user.txt"));
        var cmd = File.ReadAllLines(Path.Combine(root, "2026-10-03_16-19_cmd.txt"));
        var connection = File.ReadAllLines(Path.Combine(root, "2026-10-03_16-19_connections.txt"));

        var death = Assert.IsType<PlayerLogEvent>(PzLogParser.Parse("PerkLog", perk[^1], "perk:7", FixtureZone));
        var connected = Assert.IsType<PlayerLogEvent>(PzLogParser.Parse("user", user[1], "user:2", FixtureZone));
        var position = Assert.IsType<PlayerLogEvent>(PzLogParser.Parse("cmd", cmd[^1], "cmd:4", FixtureZone));
        var noise = Assert.IsType<PlayerLogEvent>(PzLogParser.Parse("cmd", cmd[2], "cmd:3", FixtureZone));
        var disconnected = Assert.IsType<PlayerLogEvent>(PzLogParser.Parse("connections", connection[^1], "connections:2", FixtureZone));

        Assert.Equal(PlayerLogEventKind.Died, death.Kind);
        Assert.Equal("bia", death.Username);
        Assert.Equal(7, death.HoursSurvived);
        Assert.Equal((10619, 9663, 0), (death.X, death.Y, death.Z));
        Assert.Equal(PlayerLogEventKind.Connected, connected.Kind);
        Assert.Equal(PlayerLogEventKind.Position, position.Kind);
        Assert.Equal(PlayerLogEventKind.Noise, noise.Kind);
        Assert.Equal(PlayerLogEventKind.Disconnected, disconnected.Kind);
    }

    [Fact]
    public async Task Store_DeduplicatesRealDeathAndBuildsActivity()
    {
        using var directory = new TemporaryDirectory();
        var now = new DateTimeOffset(2026, 10, 4, 2, 0, 0, TimeSpan.Zero);
        var store = new JsonCardinalStore(new TestPaths(directory.Path), new FixedTimeProvider(now, FixtureZone));
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cardinal");
        var events = new List<PlayerLogEvent>();
        foreach (var (file, type) in new[] { ("2026-10-03_16-19_PerkLog.txt", "PerkLog"), ("2026-10-03_16-19_user.txt", "user"), ("2026-10-03_16-19_cmd.txt", "cmd") })
        {
            var lines = File.ReadAllLines(Path.Combine(root, file));
            events.AddRange(lines.Select((line, index) => PzLogParser.Parse(type, line, $"{file}:{index}", FixtureZone)).OfType<PlayerLogEvent>());
        }

        await store.ApplyLogEventsAsync(events);
        await store.ApplyLogEventsAsync(events);
        var dashboard = await store.ReadAsync();

        var ana = Assert.Single(dashboard.Players, player => player.Username == "ana");
        var bia = Assert.Single(dashboard.Players, player => player.Username == "bia");
        Assert.Equal(1, ana.Deaths);
        Assert.Equal(16, ana.SurvivalRecordHours);
        Assert.InRange(ana.PlayedThisWeek.TotalMinutes, 57, 58);
        Assert.NotNull(ana.LastNoiseAt);
        Assert.Equal(1, bia.Deaths);
        Assert.Equal(7, bia.SurvivalRecordHours);
        Assert.Equal((10619, 9663, 0), (bia.X, bia.Y, bia.Z));
    }

    [Fact]
    public async Task Collector_UsesOffsetsAndSurvivesArchivedLayout()
    {
        using var directory = new TemporaryDirectory();
        var dataPath = Path.Combine(directory.Path, "Zomboid");
        var archive = Path.Combine(dataPath, "Logs", "logs_2026-10-03_16-19");
        Directory.CreateDirectory(archive);
        foreach (var name in new[] { "2026-10-03_16-19_PerkLog.txt", "2026-10-03_16-19_user.txt", "2026-10-03_16-19_cmd.txt" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cardinal", name), Path.Combine(archive, name));
        var now = new DateTimeOffset(2026, 10, 4, 2, 0, 0, TimeSpan.Zero);
        var paths = new TestPaths(directory.Path);
        var time = new FixedTimeProvider(now, FixtureZone);
        var store = new JsonCardinalStore(paths, time);
        var administration = new ServerAdministrationService(new FakeRcon("Players connected (0):\n"), new NullAudit());
        var options = Options.Create(new ManagerOptions { DataRoot = directory.Path, ManagedServer = Managed(dataPath) });
        var collector = new CardinalLogCollector(options, paths, store, administration, time, NullLogger<CardinalLogCollector>.Instance);

        await collector.CollectAsync();
        await collector.CollectAsync();
        var dashboard = await store.ReadAsync();

        Assert.Equal(2, dashboard.Players.Count);
        Assert.Equal(2, dashboard.Players.Sum(player => player.Deaths));
        Assert.True(File.Exists(Path.Combine(directory.Path, "cardinal", "log-offsets.json")));
    }

    [Fact]
    public void Policy_BlocksEveryRequestedWeakState()
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var settings = new CardinalSettings();
        var player = new CardinalPlayerSnapshot
        {
            Username = "ana", HoursSurvived = 10, HealthPercent = 55, Asleep = true, Sitting = true,
            Bleeding = true, Fracture = true, Infected = true, Zombies30 = 12,
            InVehicle = true, VehicleSpeed = 20, HasMeleeWeapon = false, WeaponAmmo = 0, InventoryAmmo = 0,
            Moodles = new() { Pain = 3, Tired = 3, Hunger = 3, Thirst = 3, Sick = 3, Drunk = 1 }
        };
        var dashboard = new CardinalDashboard(settings, new() { Timestamp = now, Players = [player] },
            [new() { Username = "ana", Online = true, ConnectedAt = now.AddHours(-1) }], [], new(0, 0, 0, DateOnly.FromDateTime(now.Date)));

        var blocks = CardinalPolicy.GetBlocks(settings, dashboard, player, CardinalActionKind.HordaDirecionada, now);

        Assert.Contains(blocks, value => value.Contains("dormindo"));
        Assert.Contains(blocks, value => value.Contains("fraco"));
        Assert.Contains(blocks, value => value.Contains("zumbis"));
        Assert.Contains(blocks, value => value.Contains("munição"));
        Assert.Contains(blocks, value => value.Contains("veículo"));
    }

    [Fact]
    public void Settings_DefaultToOffAndRejectUnsafeLimits()
    {
        var settings = new CardinalSettings().Validate();
        Assert.Equal(CardinalMode.Desligado, settings.Mode);
        Assert.Equal(15, settings.MaximumHordePerPlayer);
        Assert.Equal(30, settings.MaximumHordeTotal);
        Assert.All(new[] { settings.ProtectSleeping, settings.ProtectResting, settings.ProtectWeakPlayers,
            settings.ProtectUnarmedPlayers, settings.ProtectMovingVehicles }, Assert.True);
        Assert.Throws<ArgumentException>(() => (settings with { MaximumHordeTotal = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (settings with { RareItemAllowList = ["Base.Valid", "bad item;quit"] }).Validate());
        Assert.Throws<ArgumentException>(() => CardinalRequestValidator.Validate(new()
            { Kind = CardinalActionKind.HordaDirecionada, Player = "ana", Amount = 31, Distance = 30 }, settings));
        Assert.Throws<ArgumentException>(() => CardinalRequestValidator.Validate(new()
            { Kind = CardinalActionKind.Mensagem, Message = "ok\nquit", Distance = 30 }, settings));
    }

    private static ManagedServerOptions Managed(string dataPath) => new()
    {
            DisplayName = "Test server", ServiceName = "pzserver.service", InstallPath = dataPath,
            DataPath = dataPath, ServerName = "servertest", ConsoleFifo = Path.Combine(dataPath, "console"), BackupsPath = Path.Combine(dataPath, "backups")
    };
    [Fact]
    public async Task ResultCollector_RecordsResultAndCleansQueueActionFirst()
    {
        using var directory = new TemporaryDirectory();
        var data = Path.Combine(directory.Path, "Zomboid");
        var queue = Path.Combine(data, "Lua", "cardinal", "acoes");
        Directory.CreateDirectory(queue);
        var store = new JsonCardinalStore(new TestPaths(directory.Path), TimeProvider.System);
        await store.AppendTimelineAsync(new CardinalTimelineEvent { Id = "acao1", Timestamp = DateTimeOffset.UtcNow, Action = CardinalActionKind.Airdrop, Player = "ana" });
        File.WriteAllText(Path.Combine(queue, "acao1.txt"), "{}");
        File.WriteAllText(Path.Combine(queue, "resultado-acao1.txt"), "{\"id\":\"acao1\",\"success\":true,\"message\":\"airdrop com fumaça ao norte\"}");
        var options = Options.Create(new ManagerOptions { DataRoot = directory.Path, ManagedServer = new ManagedServerOptions
        {
            DisplayName = "Test server", ServiceName = "pzserver.service", InstallPath = Path.Combine(directory.Path, "pz"), DataPath = data,
            ServerName = "servertest", ConsoleFifo = Path.Combine(directory.Path, "console"), BackupsPath = Path.Combine(directory.Path, "backups")
        }});
        var collector = new CardinalBridgeResultCollector(options, store, new FakeRcon(""), new NullAudit(),
            NullLogger<CardinalBridgeResultCollector>.Instance);

        await collector.CollectAsync();
        var dashboard = await store.ReadAsync();

        Assert.Contains(dashboard.Timeline, item => item.Id == "acao1-resultado" && item.Status == CardinalEventStatus.Executado);
        Assert.Empty(Directory.GetFiles(queue));
    }

    private sealed class NullAudit : IAuditLog { public Task WriteAsync(string userName, string action, string result, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class FakeRcon(string output) : IRconClient
    {
        public Task<bool> CanAuthenticateAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default) => Task.FromResult(output);
    }
    private sealed class FixedTimeProvider(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => zone;
    }
    private sealed class TestPaths(string root) : IManagerPaths
    {
        public string DataRoot => root;
        public string DatabasePath => Path.Combine(root, "manager.db");
        public string InstancesRoot => Path.Combine(root, "instances");
        public string SteamCmdRoot => Path.Combine(root, "steamcmd");
        public string BackupsRoot => Path.Combine(root, "backups");
        public string LogsRoot => Path.Combine(root, "logs");
    }
    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PzManager.Cardinal-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
