using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Configuration;
using PzServerManager.Application.Mods;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Tests;

public sealed class SandboxSettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_GeneratesVanillaAndActiveModOptionsWithPtBrFallback()
    {
        using var environment = new SandboxEnvironment();
        var service = environment.CreateService();

        var snapshot = await service.LoadAsync(
            environment.SandboxPath, environment.IniPath, environment.InstallPath, "42.21");

        var water = Assert.Single(snapshot.Options, option => option.Path == "WaterShut");
        var freePoints = Assert.Single(snapshot.Options, option => option.Path == "CharacterFreePoints");
        var encounterChance = Assert.Single(snapshot.Options, option => option.Path == "ExampleNpc.EncounterChance");
        Assert.Equal("Corte de Água — faixa predefinida", water.Label);
        Assert.True(water.IsNewWorldOnly);
        Assert.Equal("Pontos Extras de Traços", freePoints.Label);
        Assert.Equal("40", encounterChance.Value);
        Assert.Equal("25", encounterChance.DefaultValue);
        Assert.Equal("Example NPC", encounterChance.SourceLabel);
        Assert.Contains("Chance de encontro", encounterChance.Label);
    }

    [Fact]
    public async Task SaveAsync_WritesBackupAndAuditAndDetectsDiskConflict()
    {
        using var environment = new SandboxEnvironment();
        var audit = new RecordingAuditLog();
        var service = environment.CreateService(audit);
        var snapshot = await service.LoadAsync(
            environment.SandboxPath, environment.IniPath, environment.InstallPath, "42.21");
        var values = snapshot.Options.ToDictionary(option => option.Path, option => option.Value, StringComparer.Ordinal);
        values["ExampleNpc.EncounterChance"] = "41";

        var changes = await service.SaveAsync(environment.SandboxPath, snapshot, values, "admin");

        Assert.Equal("41", SandboxVarsDocument.Parse(await File.ReadAllTextAsync(environment.SandboxPath)).Get("ExampleNpc.EncounterChance"));
        Assert.Single(changes);
        Assert.Single(audit.Entries);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(environment.SandboxPath)!, "*.bak"));

        var staleSnapshot = await service.LoadAsync(
            environment.SandboxPath, environment.IniPath, environment.InstallPath, "42.21");
        await File.AppendAllTextAsync(environment.SandboxPath, Environment.NewLine);
        await Assert.ThrowsAsync<ServerSettingsConflictException>(() =>
            service.SaveAsync(environment.SandboxPath, staleSnapshot, values, "admin"));
    }

    private sealed class RecordingAuditLog : IAuditLog
    {
        public List<(string Actor, string Action, string Result)> Entries { get; } = [];
        public Task WriteAsync(string userName, string action, string result, CancellationToken cancellationToken = default)
        {
            Entries.Add((userName, action, result));
            return Task.CompletedTask;
        }
    }

    private sealed class SandboxEnvironment : IDisposable
    {
        public SandboxEnvironment()
        {
            Root = Path.Combine(Path.GetTempPath(), $"PzSandboxServiceTests-{Guid.NewGuid():N}");
            InstallPath = Path.Combine(Root, "server");
            Directory.CreateDirectory(InstallPath);
            SandboxPath = Path.Combine(Root, "servertest_SandboxVars.lua");
            IniPath = Path.Combine(Root, "servertest.ini");
            File.Copy(Path.Combine(Fixtures, "sandbox", "servertest_SandboxVars.lua"), SandboxPath);
            File.WriteAllText(IniPath, "Mods=ExampleNpc\nWorkshopItems=1000000004\nMap=Muldraugh, KY");

            var vanillaRoot = Path.Combine(InstallPath, "media", "lua", "shared", "Translate");
            Directory.CreateDirectory(Path.Combine(vanillaRoot, "EN"));
            Directory.CreateDirectory(Path.Combine(vanillaRoot, "PTBR"));
            File.Copy(Path.Combine(Fixtures, "sandbox", "vanilla-Sandbox-EN.json"), Path.Combine(vanillaRoot, "EN", "Sandbox.json"));
            File.Copy(Path.Combine(Fixtures, "sandbox", "vanilla-Sandbox-PTBR.json"), Path.Combine(vanillaRoot, "PTBR", "Sandbox.json"));

            var sourceItem = Path.Combine(Fixtures, "workshop", "1000000004");
            var targetItem = Path.Combine(InstallPath, "steamapps", "workshop", "content", "108600", "1000000004");
            CopyDirectory(sourceItem, targetItem);
        }

        private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");
        public string Root { get; }
        public string InstallPath { get; }
        public string SandboxPath { get; }
        public string IniPath { get; }

        public SandboxSettingsService CreateService(IAuditLog? audit = null) => new(
            new SandboxVarsFileStore(), new ServerIniFileStore(), audit ?? new RecordingAuditLog(), new WorkshopItemInspector());

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
