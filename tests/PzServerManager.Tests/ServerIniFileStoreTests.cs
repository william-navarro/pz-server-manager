using System.Text;
using PzServerManager.Application.Configuration;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Tests;

public sealed class ServerIniFileStoreTests
{
    [Fact]
    public async Task SaveAsync_CreatesBackupAndReplacesFileWithoutLeavingTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "server.ini");
        await File.WriteAllTextAsync(path, "A=antigo", new UTF8Encoding(false));
        var document = ServerIniDocument.Parse("A=novo\nB=2");
        var store = new ServerIniFileStore();

        await store.SaveAsync(path, document);

        Assert.Equal("A=novo\nB=2", await File.ReadAllTextAsync(path));
        var backup = Assert.Single(Directory.GetFiles(directory.Path, "server.ini.*.bak"));
        Assert.Equal("A=antigo", await File.ReadAllTextAsync(backup));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_RetainsOnlyTenNewestBackups()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "server.ini");
        await File.WriteAllTextAsync(path, "Version=0");
        var store = new ServerIniFileStore();

        for (var version = 1; version <= 11; version++)
        {
            await store.SaveAsync(path, ServerIniDocument.Parse($"Version={version}"));
        }

        var backups = Directory.GetFiles(directory.Path, "server.ini.*.bak");
        Assert.Equal(10, backups.Length);
        Assert.DoesNotContain(backups, backup => File.ReadAllText(backup) == "Version=0");
        Assert.Equal("Version=11", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SaveAsync_WritesUtf8WithoutBom()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "server.ini");
        var store = new ServerIniFileStore();

        await store.SaveAsync(path, ServerIniDocument.Parse("Nome=São Paulo"));

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal("Nome=São Paulo", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task LoadAsync_ReadsUtf8Document()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "server.ini");
        await File.WriteAllTextAsync(path, "Nome=São Paulo\n", new UTF8Encoding(false));
        var store = new ServerIniFileStore();

        var document = await store.LoadAsync(path);

        Assert.Equal("São Paulo", document.Get("Nome"));
        Assert.Equal("Nome=São Paulo\n", document.ToText());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"PzServerManager.Tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
