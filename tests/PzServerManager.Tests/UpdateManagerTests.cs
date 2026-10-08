using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Automation;
using PzServerManager.Application.Backups;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Application.Metrics;
using PzServerManager.Application.Mods;
using PzServerManager.Application.Rcon;
using PzServerManager.Application.Updates;
using PzServerManager.Infrastructure.Configuration;
using PzServerManager.Infrastructure.Updates;

namespace PzServerManager.Tests;

public sealed class UpdateManagerTests
{
    [Fact]
    public async Task CheckAndApplyMods_CompareTimestampsBackUpAndRestart()
    {
        using var environment = new EnvironmentFixture();
        var controller = new RecordingController();
        var backups = new RecordingBackups(new(true, false, "ok", "backup criado: /opt/pzserver/backups/servertest-20261004-120000-atualizacao-mods.tar.gz"));
        var metrics = new RecordingMetrics();
        var manager = environment.CreateManager(controller, backups, new RecordingSystemUpdate(), metrics);

        var checkedState = await manager.CheckNowAsync("admin");
        await manager.ApplyAsync("admin", UpdateTarget.Mods);
        var finalState = await manager.GetOverviewAsync();

        var mod = Assert.Single(checkedState.OutdatedMods);
        Assert.Equal("123", mod.ItemId);
        Assert.True(checkedState.IsGameOutdated);
        Assert.Equal("100", checkedState.InstalledGameBuild);
        Assert.Equal("200", checkedState.PublishedGameBuild);
        Assert.Equal("atualizacao-mods", backups.Reason);
        Assert.True(controller.Restarted);
        Assert.Empty(finalState.OutdatedMods);
        Assert.True(finalState.IsGameOutdated);
        Assert.Equal("servertest-20261004-120000-atualizacao-mods.tar.gz", Assert.Single(finalState.History).BackupFileName);
        Assert.Single(metrics.Events);
    }

    [Fact]
    public async Task ApplyMods_WhenBackupFails_DoesNotRestartAndRecordsFailure()
    {
        using var environment = new EnvironmentFixture();
        var controller = new RecordingController();
        var manager = environment.CreateManager(controller,
            new RecordingBackups(new(false, false, "disco cheio", "erro")), new RecordingSystemUpdate(), new RecordingMetrics());
        await manager.CheckNowAsync("admin");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ApplyAsync("admin", UpdateTarget.Mods));
        var state = await manager.GetOverviewAsync();

        Assert.Contains("backup falhou", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(controller.Restarted);
        Assert.False(Assert.Single(state.History).Succeeded);
    }

    [Fact]
    public async Task ApplyGame_UsesPrivilegedRunnerAndRecordsItsBackup()
    {
        using var environment = new EnvironmentFixture();
        var updater = new RecordingSystemUpdate
        {
            Result = new(true, "ok", "backup criado: /opt/pzserver/backups/servertest-20261004-121000-atualizacao-jogo.tar.gz")
        };
        var controller = new RecordingController();
        var manager = environment.CreateManager(controller, new RecordingBackups(new(true, false, "ok", "")), updater, new RecordingMetrics());
        await manager.CheckNowAsync("admin");

        await manager.ApplyAsync("admin", UpdateTarget.Game);
        var state = await manager.GetOverviewAsync();

        Assert.True(updater.Called);
        Assert.False(controller.Restarted);
        Assert.False(state.IsGameOutdated);
        Assert.Equal("servertest-20261004-121000-atualizacao-jogo.tar.gz", Assert.Single(state.History).BackupFileName);
    }

    [Fact]
    public async Task Check_WhenSteamCmdFails_StillReportsModsAndNeverMarksGameOutdated()
    {
        using var environment = new EnvironmentFixture();
        var manager = environment.CreateManager(new RecordingController(), new RecordingBackups(new(true, false, "ok", "")),
            new RecordingSystemUpdate(), new RecordingMetrics(), new FailingAppInfoRunner());

        var state = await manager.CheckNowAsync("admin");

        Assert.Single(state.OutdatedMods);
        Assert.False(state.IsGameOutdated);
        Assert.True(state.ModsCheckSucceeded);
        Assert.StartsWith(UpdateOverview.GameCheckErrorPrefix, state.LastError);
        Assert.Equal("123@1800000000", state.PendingSignature);
    }

    private sealed class FailingAppInfoRunner : ICommandRunner
    {
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CommandResult(1, "", "steamcmd: falha de rede"));
    }

    private sealed class EnvironmentFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"PzManager.Updates-{Guid.NewGuid():N}");
        private readonly TestPaths _paths;
        private readonly ManagerOptions _options;

        public EnvironmentFixture()
        {
            Directory.CreateDirectory(_root);
            var install = Path.Combine(_root, "pz");
            var data = Path.Combine(_root, "Zomboid");
            Directory.CreateDirectory(Path.Combine(install, "steamapps", "workshop"));
            Directory.CreateDirectory(Path.Combine(data, "Server"));
            File.WriteAllText(Path.Combine(data, "Server", "servertest.ini"), "WorkshopItems=123\n");
            File.WriteAllText(Path.Combine(install, "steamapps", "workshop", "appworkshop_108600.acf"),
                "\"AppWorkshop\" { \"WorkshopItemsInstalled\" { \"123\" { \"timeupdated\" \"1700000000\" } } }");
            File.WriteAllText(Path.Combine(install, "steamapps", "appmanifest_380870.acf"), "\"AppState\" { \"buildid\" \"100\" }");
            _paths = new(_root);
            _options = new ManagerOptions { DataRoot = _root, ManagedServer = new ManagedServerOptions
            {
                DisplayName = "Test server", ServiceName = "pzserver.service", InstallPath = install, DataPath = data,
                ServerName = "servertest", ConsoleFifo = Path.Combine(_root, "console"), BackupsPath = Path.Combine(_root, "backups")
            }};
        }

        public UpdateManager CreateManager(RecordingController controller, IBackupManager backups,
            ISystemUpdateRunner updater, IMetricsStore metrics, ICommandRunner? appInfo = null)
        {
            var audit = new NullAudit();
            var administration = new ServerAdministrationService(new EmptyRcon(), audit);
            var restart = new ServerRestartCoordinator(controller, administration, audit, new StaticSettings(), TimeProvider.System);
            return new(new ServerIniFileStore(), new Metadata(), appInfo ?? new AppInfoRunner(), _paths, backups, updater,
                restart, metrics, audit, new StaticSettings(), TimeProvider.System, Options.Create(_options));
        }

        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }

    private sealed class Metadata : IWorkshopMetadataClient
    {
        public Task<IReadOnlyDictionary<string, WorkshopItemMetadata>> GetAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, WorkshopItemMetadata>>(new Dictionary<string, WorkshopItemMetadata>
            { ["123"] = new("123", "Meu Mod", DateTimeOffset.FromUnixTimeSeconds(1800000000), 1, []) });
    }
    private sealed class AppInfoRunner : ICommandRunner
    {
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CommandResult(0, "\"380870\" { \"depots\" { \"branches\" { \"public\" { \"buildid\" \"200\" } } } }", ""));
    }
    private sealed class EmptyRcon : IRconClient
    {
        public Task<bool> CanAuthenticateAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default) => Task.FromResult("Players connected (0):\n");
    }
    private sealed class StaticSettings : IAutomationSettingsStore
    {
        public Task<AutomationSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AutomationSettings());
        public Task SaveAsync(AutomationSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class RecordingController : IServerController
    {
        public bool Restarted { get; private set; }
        public Task<ManagedServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestartAsync(CancellationToken cancellationToken = default) { Restarted = true; return Task.CompletedTask; }
        public Task StartAsync(string userName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAsync(string userName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestartAsync(string userName, CancellationToken cancellationToken = default) { Restarted = true; return Task.CompletedTask; }
    }
    private sealed class RecordingBackups(BackupOperationResult result) : IBackupManager
    {
        public string? Reason { get; private set; }
        public bool IsOperationInProgress => false; public string? CurrentOperation => null;
        public Task<BackupOverview> GetOverviewAsync(CancellationToken cancellationToken = default) => Task.FromResult(new BackupOverview([], null, []));
        public Task<BackupOperationResult> CreateBackupAsync(string actor, CancellationToken cancellationToken = default) => Task.FromResult(result);
        public Task<BackupOperationResult> CreateBackupAsync(string actor, string reason, CancellationToken cancellationToken = default) { Reason = reason; return Task.FromResult(result); }
        public Task<BackupOperationResult> RestoreAsync(string actor, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BackupOperationResult> ResetWorldAsync(string actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class RecordingSystemUpdate : ISystemUpdateRunner
    {
        public bool Called { get; private set; }
        public bool IsRunning => false;
        public SystemUpdateResult Result { get; set; } = new(true, "ok", "");
        public Task<SystemUpdateResult> RunAsync(CancellationToken cancellationToken = default) { Called = true; return Task.FromResult(Result); }
    }
    private sealed class RecordingMetrics : IMetricsStore
    {
        public List<MetricEvent> Events { get; } = [];
        public Task AppendSampleAsync(MetricSample sample, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AppendEventAsync(MetricEvent metricEvent, CancellationToken cancellationToken = default) { Events.Add(metricEvent); return Task.CompletedTask; }
        public Task<MetricsSnapshot> ReadAsync(DateTimeOffset since, CancellationToken cancellationToken = default) => Task.FromResult(new MetricsSnapshot([], Events));
    }
    private sealed class NullAudit : IAuditLog
    {
        public Task WriteAsync(string userName, string action, string result, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class TestPaths(string root) : IManagerPaths
    {
        public string DataRoot => root; public string DatabasePath => Path.Combine(root, "db");
        public string InstancesRoot => Path.Combine(root, "instances"); public string SteamCmdRoot => Path.Combine(root, "steamcmd");
        public string BackupsRoot => Path.Combine(root, "backups"); public string LogsRoot => Path.Combine(root, "logs");
    }
}
