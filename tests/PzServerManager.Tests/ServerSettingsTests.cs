using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Configuration;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Tests;

public sealed class ServerSettingsTests
{
    [Fact]
    public void Catalog_GroupsInfersTypesAndReadsLimitsFromFixture()
    {
        var catalog = ServerSettingsCatalog.Create(ServerIniDocument.Parse(ReadFixture()));
        var pvp = Assert.Single(catalog, item => item.Key == "PVP");
        Assert.Equal("PvP", pvp.Group);
        Assert.Equal(ServerSettingValueType.Boolean, pvp.ValueType);
        var players = Assert.Single(catalog, item => item.Key == "MaxPlayers");
        Assert.Equal("Jogadores", players.Group);
        Assert.Equal(ServerSettingValueType.Integer, players.ValueType);
        Assert.Equal(1m, players.Constraints.Minimum);
        Assert.Equal(254m, players.Constraints.Maximum);
        Assert.Equal("32", players.Constraints.DefaultValue);
        var voice = Assert.Single(catalog, item => item.Key == "VoiceMinDistance");
        Assert.Equal(ServerSettingValueType.Decimal, voice.ValueType);
        Assert.Equal(0m, voice.Constraints.Minimum);
        Assert.Equal(100000m, voice.Constraints.Maximum);
    }

    [Theory]
    [InlineData("Mods")]
    [InlineData("WorkshopItems")]
    [InlineData("Map")]
    [InlineData("RCONPort")]
    [InlineData("RCONPassword")]
    [InlineData("DefaultPort")]
    [InlineData("UDPPort")]
    public void Catalog_ProtectsOperationalKeys(string key)
    {
        Assert.True(ServerSettingsCatalog.IsProtected(key, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void ValidateValue_EnforcesFixtureLimits()
    {
        var maxPlayers = Assert.Single(ServerSettingsCatalog.Create(ServerIniDocument.Parse(ReadFixture())), item => item.Key == "MaxPlayers");
        Assert.Throws<ArgumentOutOfRangeException>(() => ServerSettingsCatalog.ValidateValue(maxPlayers, "255"));
        ServerSettingsCatalog.ValidateValue(maxPlayers, "32");
    }

    [Fact]
    public async Task SaveAsync_RefusesFileChangedSinceOpen()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "server.ini");
        await File.WriteAllTextAsync(path, "PVP=true\nMaxPlayers=32");
        var service = new ServerSettingsService(new ServerIniFileStore(), new NullAuditLog());
        var snapshot = await service.LoadAsync(path);
        await File.WriteAllTextAsync(path, "PVP=true\nMaxPlayers=16");

        await Assert.ThrowsAsync<ServerSettingsConflictException>(() =>
            service.SaveAsync(path, snapshot.OriginalText, new Dictionary<string, string> { ["PVP"] = "false" }, "admin"));
        Assert.Equal("PVP=true\nMaxPlayers=16", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SaveAsync_RejectsProtectedKey()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "server.ini");
        await File.WriteAllTextAsync(path, "Map=Muldraugh, KY");
        var service = new ServerSettingsService(new ServerIniFileStore(), new NullAuditLog());
        var snapshot = await service.LoadAsync(path);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(path, snapshot.OriginalText, new Dictionary<string, string> { ["Map"] = "West Point, KY" }, "admin"));
    }

    private static string ReadFixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "servertest.ini"));
    private sealed class NullAuditLog : IAuditLog { public Task WriteAsync(string userName, string action, string result, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PzSettings-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
