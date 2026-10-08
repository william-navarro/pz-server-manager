using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Configuration;
using PzServerManager.Application.Mods;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Tests;

public sealed class ModManagerTests
{
    private static string WorkshopFixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures", "workshop");
    private readonly WorkshopItemInspector _inspector = new();

    [Fact]
    public void Parser_HandlesCrLfRequiresBackslashesAndUnknownKeys()
    {
        var parsed = ModInfoParser.Parse("name=Meu Mod\r\nid=1000000001/MeuMod\r\nrequire=\\Base, \\ExampleCounter\r\ndescription=texto=mantido\r\nunknown=value\r\nversionMin=42.17\r\nversionMax=42.30\r\nmodversion=2.1\r\n");

        Assert.Equal("1000000001/MeuMod", parsed.Id);
        Assert.Equal("Meu Mod", parsed.Name);
        Assert.Equal(["Base", "ExampleCounter"], parsed.Requires);
        Assert.Equal("42.17", parsed.VersionMin);
        Assert.Equal("42.30", parsed.VersionMax);
        Assert.Equal("2.1", parsed.ModVersion);
    }

    [Theory]
    [InlineData("1000000001", "ExampleTraits", "42.20")]
    [InlineData("1000000001", "ExampleTraitsDynamic", "42.17")]
    [InlineData("1000000001", "ExampleTraitsPrepared", "common")]
    [InlineData("1000000002", "ExampleCounter", "common")]
    [InlineData("1000000003", "ExampleClothing", "42.17")]
    public void Inspector_SelectsHighestCompatibleVersionFolder(string itemId, string modId, string expectedFolder)
    {
        var item = Inspect(itemId);
        Assert.Equal(expectedFolder, Assert.Single(item.Mods, mod => mod.Id == modId).VersionFolder);
    }

    [Theory]
    [InlineData("1000000001", "ExampleFuture|ExampleTraits|ExampleTraitsDynamic|ExampleTraitsPrepared")]
    [InlineData("1000000002", "ExampleCounter")]
    [InlineData("1000000003", "ExampleClothing")]
    [InlineData("1000000004", "ExampleNpc")]
    public void Inspector_InspectsEverySyntheticFixture(string itemId, string expectedIds)
    {
        var actual = Inspect(itemId).Mods.Select(mod => mod.Id).OrderBy(id => id, StringComparer.Ordinal);
        Assert.Equal(expectedIds.Split('|').OrderBy(id => id, StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Inspector_IgnoresB41RootWhenVersionFolderExists()
    {
        var mod = Assert.Single(Inspect("1000000001").Mods, candidate => candidate.Id == "ExampleTraits");
        Assert.NotEqual("LegacyExampleTraits", mod.Id);
        Assert.Equal("42.20", mod.VersionFolder);
    }

    [Fact]
    public void Inspector_UsesRootOnlyWhenThereIsNoB42Layout()
    {
        using var directory = new TemporaryDirectory();
        var modPath = Path.Combine(directory.Path, "mods", "OldButOnly");
        Directory.CreateDirectory(modPath);
        File.WriteAllText(Path.Combine(modPath, "mod.info"), "name=Único\nid=OnlyRoot");

        var item = _inspector.Inspect("1", directory.Path);

        Assert.Equal("raiz", Assert.Single(item.Mods).VersionFolder);
    }

    [Fact]
    public void Inspector_NormalizesRequiresAndChecksCompatibility()
    {
        var dynamicTraits = Assert.Single(Inspect("1000000001").Mods, mod => mod.Id == "ExampleTraitsDynamic");
        Assert.Equal(["ExampleTraits", "ExampleCounter"], dynamicTraits.Requires);
        Assert.True(dynamicTraits.IsCompatible);
        var future = Assert.Single(Inspect("1000000001").Mods, mod => mod.Id == "ExampleFuture");
        Assert.False(future.IsCompatible);
    }

    [Fact]
    public void Dependencies_ReportMissingAndKnownProvider()
    {
        var items = new[] { Inspect("1000000001"), Inspect("1000000002") };

        var issues = ModDependencyResolver.FindMissing(["ExampleTraitsDynamic"], items);

        Assert.Contains(issues, issue => issue.RequiredModId == "ExampleTraits" && issue.ProviderItemIds.Contains("1000000001"));
        Assert.Contains(issues, issue => issue.RequiredModId == "ExampleCounter" && issue.ProviderItemIds.SequenceEqual(["1000000002"]));
    }

    [Fact]
    public void Dependencies_ReportUnknownProvider()
    {
        var issues = ModDependencyResolver.FindMissing(["ExampleClothing"], [Inspect("1000000003")]);
        var issue = Assert.Single(issues);
        Assert.Equal("MissingBase", issue.RequiredModId);
        Assert.Empty(issue.ProviderItemIds);
    }

    [Fact]
    public void TopologicalSort_PutsDependenciesFirstAndKeepsUnrestrictedOrder()
    {
        var items = new[] { Inspect("1000000001"), Inspect("1000000002") };
        var result = ModDependencyResolver.Sort(
            ["ExampleTraitsDynamic", "ExampleCounter", "ExampleTraits", "ExampleTraitsPrepared"], items);

        Assert.True(result.Success);
        Assert.Equal(["ExampleCounter", "ExampleTraits", "ExampleTraitsDynamic", "ExampleTraitsPrepared"], result.OrderedModIds);
    }

    [Fact]
    public void TopologicalSort_DetectsCycle()
    {
        var item = new WorkshopItemInspection("1", "x", [CreateMod("A", "B"), CreateMod("B", "A")]);
        var result = ModDependencyResolver.Sort(["A", "B"], [item]);

        Assert.False(result.Success);
        Assert.Equal(["A", "B"], result.CycleModIds);
    }

    [Theory]
    [InlineData("123", "123")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=123", "123")]
    [InlineData("http://www.steamcommunity.com/sharedfiles/filedetails?id=123&searchtext=x", "123")]
    public void WorkshopIdParser_ParsesIdsAndUrlVariants(string input, string expected) =>
        Assert.Equal(expected, WorkshopIdParser.Parse(input));

    [Fact]
    public void WorkshopIdParser_ParsesSeveralAndRejectsRepeatedOrInvalid()
    {
        Assert.Equal(["123", "456"], WorkshopIdParser.ParseMany("123,\nhttps://steamcommunity.com/?id=456"));
        Assert.Throws<ArgumentException>(() => WorkshopIdParser.ParseMany("123 123"));
        Assert.Throws<FormatException>(() => WorkshopIdParser.Parse("https://example.com/?id=123"));
        Assert.Throws<FormatException>(() => WorkshopIdParser.Parse("abc"));
    }

    [Fact]
    public async Task Service_BuildsBeforeAfterAndSavesAllThreeLists()
    {
        using var directory = new TemporaryDirectory();
        var iniPath = Path.Combine(directory.Path, "server.ini");
        await File.WriteAllTextAsync(iniPath, "WorkshopItems=1\nMods=Old\nMap=Muldraugh, KY\n");
        var audit = new RecordingAuditLog();
        var service = new ModManagerService(new ServerIniFileStore(), new TestPaths(directory.Path), new EmptyMetadataClient(), audit, _inspector);
        var snapshot = await service.LoadAsync(iniPath);

        var change = service.BuildChange(snapshot, ["1", "2"], ["Old", "New"], ["New Map", "Muldraugh, KY"]);
        await service.SaveAsync(iniPath, snapshot, change.WorkshopItemsAfter, change.ModsAfter, change.MapsAfter, "admin");

        Assert.Equal(["1"], change.WorkshopItemsBefore);
        Assert.Equal(["1", "2"], change.WorkshopItemsAfter);
        var saved = await File.ReadAllTextAsync(iniPath);
        Assert.Contains("WorkshopItems=1;2", saved);
        Assert.Contains("Mods=Old;New", saved);
        Assert.Contains("Map=New Map;Muldraugh, KY", saved);
        Assert.Contains("alterar mods", audit.Action);
    }

    [Fact]
    public async Task Service_RefusesDuplicateItemsAndModIds()
    {
        using var directory = new TemporaryDirectory();
        var iniPath = Path.Combine(directory.Path, "server.ini");
        await File.WriteAllTextAsync(iniPath, "WorkshopItems=\nMods=\nMap=Muldraugh, KY\n");
        var service = CreateService(directory.Path);
        var snapshot = await service.LoadAsync(iniPath);

        Assert.Throws<ArgumentException>(() => service.BuildChange(snapshot, ["1", "1"], [], ["Muldraugh, KY"]));
        Assert.Throws<ArgumentException>(() => service.BuildChange(snapshot, ["1"], ["Same", "Same"], ["Muldraugh, KY"]));
    }

    [Fact]
    public async Task Service_RefusesFileChangedAfterPageWasLoaded()
    {
        using var directory = new TemporaryDirectory();
        var iniPath = Path.Combine(directory.Path, "server.ini");
        await File.WriteAllTextAsync(iniPath, "WorkshopItems=1\nMods=Old\nMap=Muldraugh, KY\n");
        var service = CreateService(directory.Path);
        var snapshot = await service.LoadAsync(iniPath);
        await File.WriteAllTextAsync(iniPath, "WorkshopItems=2\nMods=Other\nMap=Muldraugh, KY\n");

        await Assert.ThrowsAsync<ServerSettingsConflictException>(() =>
            service.SaveAsync(iniPath, snapshot, ["1"], ["New"], ["Muldraugh, KY"], "admin"));
        Assert.Contains("WorkshopItems=2", await File.ReadAllTextAsync(iniPath));
    }

    [Fact]
    public async Task Service_NetworkMetadataFailureDoesNotBreakPageLoad()
    {
        using var directory = new TemporaryDirectory();
        var iniPath = Path.Combine(directory.Path, "server.ini");
        await File.WriteAllTextAsync(iniPath, "WorkshopItems=1\nMods=\nMap=Muldraugh, KY\n");
        var service = new ModManagerService(new ServerIniFileStore(), new TestPaths(directory.Path), new FailingMetadataClient(), new RecordingAuditLog(), _inspector);

        var snapshot = await service.LoadAsync(iniPath);

        Assert.Empty(snapshot.Metadata);
        Assert.Equal(["1"], snapshot.WorkshopItems);
    }

    [Fact]
    public void Maps_AreInsertedBeforeMuldraughAndCanBeRemoved()
    {
        var enabled = ModManagerService.SetMapEnabled(["West Point, KY", "Muldraugh, KY"], ["Bedford Falls"], true);
        Assert.Equal(["West Point, KY", "Bedford Falls", "Muldraugh, KY"], enabled);
        Assert.Equal(["West Point, KY", "Muldraugh, KY"], ModManagerService.SetMapEnabled(enabled, ["Bedford Falls"], false));
    }

    [Fact]
    public void Inspector_DetectsMapDirectory()
    {
        using var directory = new TemporaryDirectory();
        var modPath = Path.Combine(directory.Path, "mods", "MapMod", "42.20");
        Directory.CreateDirectory(Path.Combine(modPath, "media", "maps", "Bedford Falls"));
        File.WriteAllText(Path.Combine(modPath, "mod.info"), "name=Mapa\nid=MapMod");

        Assert.Equal(["Bedford Falls"], Assert.Single(_inspector.Inspect("1", directory.Path).Mods).MapNames);
    }

    private WorkshopItemInspection Inspect(string itemId) => _inspector.Inspect(itemId, Path.Combine(WorkshopFixtures, itemId));
    private static InspectedMod CreateMod(string id, params string[] requires) => new(id, id, requires, null, null, null, "42", [], true);
    private ModManagerService CreateService(string root) => new(new ServerIniFileStore(), new TestPaths(root), new EmptyMetadataClient(), new RecordingAuditLog(), _inspector);

    private sealed class EmptyMetadataClient : IWorkshopMetadataClient
    {
        public Task<IReadOnlyDictionary<string, WorkshopItemMetadata>> GetAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, WorkshopItemMetadata>>(new Dictionary<string, WorkshopItemMetadata>());
    }

    private sealed class FailingMetadataClient : IWorkshopMetadataClient
    {
        public Task<IReadOnlyDictionary<string, WorkshopItemMetadata>> GetAsync(IReadOnlyList<string> itemIds, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Sem rede.");
    }

    private sealed class RecordingAuditLog : IAuditLog
    {
        public string? Action { get; private set; }
        public Task WriteAsync(string userName, string action, string result, CancellationToken cancellationToken = default) { Action = action; return Task.CompletedTask; }
    }

    private sealed class TestPaths(string root) : IManagerPaths
    {
        public string DataRoot => root;
        public string DatabasePath => Path.Combine(root, "db");
        public string InstancesRoot => Path.Combine(root, "instances");
        public string SteamCmdRoot => Path.Combine(root, "steamcmd");
        public string BackupsRoot => Path.Combine(root, "backups");
        public string LogsRoot => Path.Combine(root, "logs");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PzMods-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
