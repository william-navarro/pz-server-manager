using PzServerManager.Application.Configuration;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Tests;

public sealed class SandboxVarsFileStoreTests
{
    [Fact]
    public async Task SaveAsync_CreatesBackupAtomicallyAndRetainsFormatting()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "servertest_SandboxVars.lua");
        const string original = "SandboxVars = {\n    A = 1,\n}";
        await File.WriteAllTextAsync(path, original);
        var document = SandboxVarsDocument.Parse(original);
        document.Set("A", "2");

        await new SandboxVarsFileStore().SaveAsync(path, document);

        Assert.Equal("SandboxVars = {\n    A = 2,\n}", await File.ReadAllTextAsync(path));
        Assert.Equal(original, await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(directory.Path, "*.bak"))));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PzSandboxTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
