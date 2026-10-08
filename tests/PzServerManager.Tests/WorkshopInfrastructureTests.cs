using System.Net;
using System.Text;
using PzServerManager.Application.Abstractions;
using PzServerManager.Infrastructure.Steam;

namespace PzServerManager.Tests;

public sealed class WorkshopInfrastructureTests
{
    [Fact]
    public async Task MetadataClient_PostsFormAndParsesDetails()
    {
        var handler = new FakeHttpHandler("""
        {"response":{"publishedfiledetails":[{"publishedfileid":"123","result":1,"title":"Meu Mod","time_updated":1700000000,"file_size":"2048","tags":[{"tag":"Build 42"},{"tag":"Multiplayer"}]}]}}
        """);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        using var client = new SteamWorkshopMetadataClient(http);

        var result = await client.GetAsync(["123", "456"]);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Contains("itemcount=2", handler.Body);
        Assert.Contains("publishedfileids%5B0%5D=123", handler.Body);
        var metadata = Assert.Single(result).Value;
        Assert.Equal("Meu Mod", metadata.Title);
        Assert.Equal(2048, metadata.SizeBytes);
        Assert.Equal(["Build 42", "Multiplayer"], metadata.Tags);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), metadata.UpdatedAt);
    }

    [Fact]
    public async Task MetadataClient_BatchesAtOneHundredItems()
    {
        var handler = new FakeHttpHandler("{\"response\":{\"publishedfiledetails\":[]}}");
        using var client = new SteamWorkshopMetadataClient(new HttpClient(handler));

        await client.GetAsync(Enumerable.Range(1, 101).Select(number => number.ToString()).ToArray());

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Downloader_UsesPanelSteamCmdAndExactArguments()
    {
        var runner = new RecordingCommandRunner();
        var paths = new TestPaths(Path.GetFullPath(Path.Combine("data", "manager")));
        var downloader = new SteamWorkshopDownloader(paths, runner);

        await downloader.DownloadAsync(["123", "456"]);

        Assert.Equal(Path.Combine(paths.DataRoot, "steamcmd", "steamcmd.sh"), runner.FileName);
        Assert.Equal([
            "+force_install_dir", Path.Combine(paths.DataRoot, "workshop"),
            "+login", "anonymous",
            "+workshop_download_item", "108600", "123",
            "+workshop_download_item", "108600", "456",
            "+quit"], runner.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(15), runner.Timeout);
    }

    private sealed class FakeHttpHandler(string responseJson) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string Body { get; private set; } = string.Empty;
        public int CallCount { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method; CallCount++;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(responseJson, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class RecordingCommandRunner : ICommandRunner
    {
        public string? FileName { get; private set; }
        public IReadOnlyList<string>? Arguments { get; private set; }
        public TimeSpan Timeout { get; private set; }
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            FileName = fileName; Arguments = arguments.ToArray(); Timeout = timeout;
            return Task.FromResult(new CommandResult(0, "Success", string.Empty));
        }
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
}
