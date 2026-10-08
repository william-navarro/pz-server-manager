namespace PzServerManager.Domain.Servers;

public sealed class ServerInstance
{
    private ServerInstance()
    {
    }

    private ServerInstance(
        Guid id,
        string name,
        string slug,
        string installationPath,
        string dataPath,
        ServerBranch branch,
        int gamePort,
        int rconPort,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Slug = slug;
        InstallationPath = installationPath;
        DataPath = dataPath;
        Branch = branch;
        GamePort = gamePort;
        RconPort = rconPort;
        Status = ServerInstanceStatus.NotInstalled;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string InstallationPath { get; private set; } = string.Empty;
    public string DataPath { get; private set; } = string.Empty;
    public ServerBranch Branch { get; private set; }
    public int GamePort { get; private set; }
    public int RconPort { get; private set; }
    public ServerInstanceStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ServerInstance Create(
        string name,
        string slug,
        string installationPath,
        string dataPath,
        ServerBranch branch,
        int gamePort,
        int rconPort,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataPath);

        var normalizedName = name.Trim();
        if (normalizedName.Length > 80)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "O nome deve possuir no máximo 80 caracteres.");
        }

        if (gamePort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(gamePort), "A porta do jogo deve estar entre 1 e 65535.");
        }

        if (rconPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(rconPort), "A porta RCON deve estar entre 1 e 65535.");
        }

        if (gamePort == rconPort)
        {
            throw new ArgumentException("A porta do jogo e a porta RCON devem ser diferentes.");
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new ServerInstance(
            Guid.NewGuid(),
            normalizedName,
            slug,
            Path.GetFullPath(installationPath),
            Path.GetFullPath(dataPath),
            branch,
            gamePort,
            rconPort,
            timestamp);
    }

    public void MarkInstalling()
    {
        Status = ServerInstanceStatus.Installing;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkStopped()
    {
        Status = ServerInstanceStatus.Stopped;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkFaulted()
    {
        Status = ServerInstanceStatus.Faulted;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
