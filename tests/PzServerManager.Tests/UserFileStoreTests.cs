using System.Text.Json;
using PzServerManager.Application.Abstractions;
using PzServerManager.Infrastructure.Authentication;

namespace PzServerManager.Tests;

public sealed class UserFileStoreTests
{
    [Fact]
    public async Task MissingFile_HasNoUsersAndRejectsLogin()
    {
        using var directory = new TemporaryDirectory();
        var store = new UserFileStore(new TestPaths(directory.Path));

        Assert.False(await store.HasUsersAsync());
        Assert.False(await store.VerifyAsync("admin", "senha"));
    }

    [Fact]
    public async Task SetPassword_CreatesUserFileAndVerifiesPassword()
    {
        using var directory = new TemporaryDirectory();
        var store = new UserFileStore(new TestPaths(directory.Path));

        await store.SetPasswordAsync("Admin", "senha inicial");

        Assert.True(await store.HasUsersAsync());
        Assert.True(await store.VerifyAsync("admin", "senha inicial"));
        var json = await File.ReadAllTextAsync(Path.Combine(directory.Path, "users.json"));
        Assert.DoesNotContain("senha inicial", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Admin", document.RootElement[0].GetProperty("Name").GetString());
    }

    [Fact]
    public async Task SetPassword_ChangesExistingUserWithoutCreatingDuplicate()
    {
        using var directory = new TemporaryDirectory();
        var store = new UserFileStore(new TestPaths(directory.Path));
        await store.SetPasswordAsync("admin", "senha antiga");

        await store.SetPasswordAsync("ADMIN", "senha nova");

        Assert.False(await store.VerifyAsync("admin", "senha antiga"));
        Assert.True(await store.VerifyAsync("admin", "senha nova"));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory.Path, "users.json")));
        Assert.Equal(1, document.RootElement.GetArrayLength());
    }

    private sealed class TestPaths(string root) : IManagerPaths
    {
        public string DataRoot => root;
        public string DatabasePath => Path.Combine(root, "data", "manager.db");
        public string InstancesRoot => Path.Combine(root, "instances");
        public string SteamCmdRoot => Path.Combine(root, "steamcmd");
        public string BackupsRoot => Path.Combine(root, "backups");
        public string LogsRoot => Path.Combine(root, "logs");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PzServerManager.Users-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
