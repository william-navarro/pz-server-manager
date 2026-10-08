using System.Text.Json;
using PzServerManager.Application.Abstractions;

namespace PzServerManager.Infrastructure.Authentication;

public sealed class UserFileStore(IManagerPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private string FilePath => Path.Combine(paths.DataRoot, "users.json");

    public async Task<bool> HasUsersAsync(CancellationToken cancellationToken = default) =>
        (await ReadUsersAsync(cancellationToken)).Count > 0;

    public async Task<bool> VerifyAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var users = await ReadUsersAsync(cancellationToken);
        var user = users.SingleOrDefault(item => item.Name.Equals(userName.Trim(), StringComparison.OrdinalIgnoreCase));
        return user is not null && PasswordHasher.Verify(password, user.PasswordHash);
    }

    public async Task SetPasswordAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var normalizedName = userName.Trim();
        if (normalizedName.Contains('\r') || normalizedName.Contains('\n'))
        {
            throw new ArgumentException("O nome do usuário não pode conter quebra de linha.", nameof(userName));
        }

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var users = await ReadUsersCoreAsync(cancellationToken);
            var existing = users.SingleOrDefault(item => item.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                users.Add(new UserRecord(normalizedName, PasswordHasher.Hash(password)));
            }
            else
            {
                existing.Name = normalizedName;
                existing.PasswordHash = PasswordHasher.Hash(password);
            }

            Directory.CreateDirectory(paths.DataRoot);
            var temporaryPath = $"{FilePath}.tmp";
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(stream, users, JsonOptions, cancellationToken);
                }

                SetPrivatePermissions(temporaryPath);
                File.Move(temporaryPath, FilePath, overwrite: true);
                SetPrivatePermissions(FilePath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<List<UserRecord>> ReadUsersAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadUsersCoreAsync(cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<List<UserRecord>> ReadUsersCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        await using var stream = new FileStream(
            FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<List<UserRecord>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private static void SetPrivatePermissions(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public sealed class UserRecord
    {
        public UserRecord(string name, string passwordHash)
        {
            Name = name;
            PasswordHash = passwordHash;
        }

        public string Name { get; set; }
        public string PasswordHash { get; set; }
    }
}
