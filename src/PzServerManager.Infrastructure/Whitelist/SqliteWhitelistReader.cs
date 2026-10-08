using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Whitelist;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.Whitelist;

public sealed class SqliteWhitelistReader(IOptions<ManagerOptions> options) : IWhitelistReader
{
    public async Task<IReadOnlyList<WhitelistAccount>> ListAsync(CancellationToken cancellationToken = default)
    {
        var managed = options.Value.ManagedServer
            ?? throw new InvalidOperationException("O servidor Linux gerenciado não foi configurado.");
        var databasePath = Path.Combine(managed.DataPath, "db", $"{managed.ServerName}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var roles = await ReadRolesAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        // Deliberadamente explícito: password e googleKey nunca são consultados.
        command.CommandText = """
            SELECT id, world, username, lastConnection, role, authType, steamid, ownerid, displayName
            FROM whitelist
            ORDER BY username COLLATE NOCASE
            """;
        var result = new List<WhitelistAccount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var rawRole = GetText(reader, 4);
            result.Add(new(
                reader.GetInt64(0),
                GetText(reader, 1),
                GetText(reader, 2) ?? string.Empty,
                ParseDate(GetText(reader, 3)),
                rawRole is not null && roles.TryGetValue(rawRole, out var roleName) ? roleName : rawRole,
                GetText(reader, 5),
                GetText(reader, 6),
                GetText(reader, 7),
                GetText(reader, 8)));
        }
        return result;
    }

    private static async Task<Dictionary<string, string>> ReadRolesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var columns = new List<RoleColumn>();
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(role)";
            await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(new(reader.GetString(1), reader.GetInt64(5) != 0));
            }
        }
        if (columns.Count == 0) return new(StringComparer.OrdinalIgnoreCase);

        var idColumn = columns.FirstOrDefault(column => column.IsPrimaryKey)?.Name
            ?? columns.FirstOrDefault(column => column.Name.Equals("id", StringComparison.OrdinalIgnoreCase))?.Name;
        var nameColumn = columns.FirstOrDefault(column => column.Name.Equals("name", StringComparison.OrdinalIgnoreCase))?.Name
            ?? columns.FirstOrDefault(column => column.Name.Equals("role", StringComparison.OrdinalIgnoreCase))?.Name
            ?? columns.FirstOrDefault(column => column.Name.Contains("name", StringComparison.OrdinalIgnoreCase))?.Name;
        if (idColumn is null || nameColumn is null) return new(StringComparer.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {QuoteIdentifier(idColumn)}, {QuoteIdentifier(nameColumn)} FROM role";
        var roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var roleReader = await command.ExecuteReaderAsync(cancellationToken);
        while (await roleReader.ReadAsync(cancellationToken))
        {
            if (!roleReader.IsDBNull(0) && !roleReader.IsDBNull(1))
            {
                roles[Convert.ToString(roleReader.GetValue(0), CultureInfo.InvariantCulture)!] =
                    Convert.ToString(roleReader.GetValue(1), CultureInfo.InvariantCulture)!;
            }
        }
        return roles;
    }

    private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
    private static string? GetText(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : null;
    private sealed record RoleColumn(string Name, bool IsPrimaryKey);
}
