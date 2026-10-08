using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PzServerManager.Infrastructure.Configuration;
using PzServerManager.Infrastructure.Whitelist;

namespace PzServerManager.Tests;

public sealed class SqliteWhitelistReaderTests
{
    [Fact]
    public async Task ListAsync_ReadsOnlyDisplayFieldsAndResolvesRoleName()
    {
        using var directory = new TemporaryDirectory();
        var dbDirectory = Path.Combine(directory.Path, "db");
        Directory.CreateDirectory(dbDirectory);
        var dbPath = Path.Combine(dbDirectory, "servertest.db");
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE role (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
                CREATE TABLE whitelist (
                    id INTEGER PRIMARY KEY, world TEXT, username TEXT, password TEXT,
                    lastConnection TEXT, role INTEGER, authType TEXT, googleKey TEXT,
                    steamid TEXT, ownerid TEXT, displayName TEXT);
                INSERT INTO role VALUES (2, 'Moderator');
                INSERT INTO whitelist VALUES (1, 'servertest', 'Ana', 'nao-ler', '2026-10-03T10:30:00Z', 2, 'pwd', 'nao-ler', '123', '456', 'Ana Silva');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var reader = new SqliteWhitelistReader(Options.Create(new ManagerOptions
        {
            ManagedServer = new ManagedServerOptions { DataPath = directory.Path, ServerName = "servertest" }
        }));
        var account = Assert.Single(await reader.ListAsync());
        Assert.Equal("Ana", account.UserName);
        Assert.Equal("Moderator", account.RoleName);
        Assert.Equal("Ana Silva", account.DisplayName);
        Assert.DoesNotContain("nao-ler", string.Join('|', account.Id, account.World, account.UserName, account.RoleName, account.DisplayName));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PzWhitelist-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
