using PzServerManager.Domain.Servers;

namespace PzServerManager.Application.Servers;

public sealed record ServerInstanceDto(
    Guid Id,
    string Name,
    string Slug,
    string InstallationPath,
    string DataPath,
    ServerBranch Branch,
    int GamePort,
    int RconPort,
    ServerInstanceStatus Status,
    DateTimeOffset CreatedAtUtc);
