using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Automation;
using PzServerManager.Application.ConfigurationHistory;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Application.Metrics;
using PzServerManager.Infrastructure.Automation;
using PzServerManager.Infrastructure.Configuration;
using PzServerManager.Infrastructure.Metrics;
using PzServerManager.Infrastructure.Steam;

namespace PzServerManager.Tests;

public sealed class Stage6CoreTests
{
    [Fact]
    public void AutomationSettings_DefaultsAndValidation_AreSafe()
    {
        var settings = new AutomationSettings();
        Assert.Equal([300, 60, 10], settings.WarningSeconds);
        Assert.False(settings.DailyRestartEnabled);
        Assert.Equal(15, settings.CheckIntervalMinutes);
        Assert.Equal(UpdateMode.Automatico, settings.UpdateMode);
        Assert.Equal([300, 60, 10], (settings with { WarningSeconds = [10, 300, 60] }).Validate().WarningSeconds);
        Assert.Throws<ArgumentException>(() => (settings with { DailyRestartTime = "25:99" }).Validate());
        Assert.Throws<ArgumentException>(() => (settings with { WarningSeconds = [60, 60] }).Validate());
    }

    [Fact]
    public async Task AutomationSettingsStore_RoundTripsAtomically()
    {
        using var directory = new TemporaryDirectory();
        var store = new AutomationSettingsStore(new TestPaths(directory.Path));
        var expected = new AutomationSettings { DailyRestartEnabled = true, DailyRestartTime = "04:15", CheckIntervalMinutes = 30, UpdateMode = UpdateMode.SoAvisar, WarningSeconds = [120, 15] };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected.DailyRestartEnabled, actual.DailyRestartEnabled);
        Assert.Equal(expected.DailyRestartTime, actual.DailyRestartTime);
        Assert.Equal(expected.CheckIntervalMinutes, actual.CheckIntervalMinutes);
        Assert.Equal(expected.UpdateMode, actual.UpdateMode);
        Assert.Equal(expected.WarningSeconds, actual.WarningSeconds);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void SteamVdfParser_ReadsWorkshopAndGameBuilds()
    {
        const string workshop = "\"AppWorkshop\" { \"WorkshopItemsInstalled\" { \"123\" { \"manifest\" \"88\" \"timeupdated\" \"1700000000\" } \"456\" { \"manifest\" \"99\" \"timeupdated\" \"1800000000\" } } }";
        const string installed = "\"AppState\" { \"appid\" \"380870\" \"buildid\" \"101\" }";
        const string published = "\"380870\" { \"depots\" { \"branches\" { \"public\" { \"buildid\" \"202\" } } } }";

        var items = SteamVdfParser.ParseWorkshopUpdates(workshop);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), items["123"]);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1800000000), items["456"]);
        Assert.Equal("101", SteamVdfParser.ParseInstalledBuild(installed));
        Assert.Equal("202", SteamVdfParser.ParsePublicBuild(published));
    }

    [Fact]
    public void LineDiff_ProducesAddedRemovedAndUnchangedLines()
    {
        var result = LineDiff.Compare("a\nb\nc", "a\nx\nc");

        Assert.Contains(result, line => line.Kind == DiffLineKind.Removed && line.Text == "b" && line.OldLineNumber == 2);
        Assert.Contains(result, line => line.Kind == DiffLineKind.Added && line.Text == "x" && line.NewLineNumber == 2);
        Assert.Equal(2, result.Count(line => line.Kind == DiffLineKind.Unchanged));
    }

    [Fact]
    public async Task ConfigurationHistory_ListsComparesRestoresAndRejectsTraversal()
    {
        using var directory = new TemporaryDirectory();
        var serverDirectory = Path.Combine(directory.Path, "Server"); Directory.CreateDirectory(serverDirectory);
        var current = Path.Combine(serverDirectory, "servertest.ini");
        await File.WriteAllTextAsync(current, "A=novo\nB=2\n");
        await File.WriteAllTextAsync(Path.Combine(serverDirectory, "servertest_SandboxVars.lua"), "return { New = true }\n");
        var backupName = "servertest.ini.20261004-010203-004.bak";
        await File.WriteAllTextAsync(Path.Combine(serverDirectory, backupName), "A=antigo\nB=2\n");
        await File.WriteAllTextAsync(Path.Combine(serverDirectory, "servertest_SandboxVars.lua.original"), "return { New = false }\n");
        await File.WriteAllTextAsync(Path.Combine(serverDirectory, "servertest.ini.invalido"), "não listar");
        var service = new ConfigurationHistoryService(Options.Create(CreateOptions(directory.Path)), new NullAudit(), new ServerOperationLock());

        var files = await service.ListAsync();
        var comparison = await service.CompareAsync(backupName);
        await service.RestoreAsync("admin", backupName);

        Assert.Equal(2, files.Count);
        Assert.Contains(files, file => file.FileName == backupName && file.Kind == ConfigurationFileKind.ServerIni);
        Assert.Contains(files, file => file.FileName == "servertest_SandboxVars.lua.original" && file.Kind == ConfigurationFileKind.SandboxVars);
        Assert.Contains(comparison.Lines, line => line.Kind == DiffLineKind.Removed && line.Text == "A=antigo");
        Assert.Equal("A=antigo\nB=2\n", await File.ReadAllTextAsync(current));
        Assert.True(Directory.GetFiles(serverDirectory, "servertest.ini.*.bak").Length >= 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompareAsync("../servertest.ini"));
    }

    [Fact]
    public async Task JsonMetricsStore_RotatesAtSevenDaysAndFiltersRange()
    {
        using var directory = new TemporaryDirectory();
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var store = new JsonMetricsStore(new TestPaths(directory.Path), new FixedTimeProvider(now));
        await store.AppendSampleAsync(new(now.AddDays(-8), 1, 1, 1));
        await store.AppendSampleAsync(new(now.AddHours(-2), 2, 2, 2));
        await store.AppendSampleAsync(new(now, 3, 3, 3));
        await store.AppendEventAsync(new(now, "reinicio", "Servidor reiniciado"));

        var result = await store.ReadAsync(now.AddHours(-1));
        var retained = await store.ReadAsync(now.AddDays(-10));

        Assert.Equal(3, Assert.Single(result.Samples).MemoryBytes);
        Assert.Single(result.Events);
        Assert.Equal(2, retained.Samples.Count);
    }

    private static ManagerOptions CreateOptions(string root) => new()
    {
        DataRoot = root,
            ManagedServer = new ManagedServerOptions { DisplayName = "Test server", ServiceName = "pzserver.service", InstallPath = root,
                DataPath = root, ServerName = "servertest", ConsoleFifo = Path.Combine(root, "console"), BackupsPath = Path.Combine(root, "backups") }
    };

    private sealed class NullAudit : IAuditLog
    {
        public Task WriteAsync(string userName, string action, string result, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
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
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PzManager.Stage6-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
