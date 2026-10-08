namespace PzServerManager.Application.Whitelist;

public sealed record WhitelistAccount(
    long Id,
    string? World,
    string UserName,
    DateTimeOffset? LastConnection,
    string? RoleName,
    string? AuthType,
    string? SteamId,
    string? OwnerId,
    string? DisplayName);
