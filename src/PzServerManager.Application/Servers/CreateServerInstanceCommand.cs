using PzServerManager.Domain.Servers;

namespace PzServerManager.Application.Servers;

public sealed record CreateServerInstanceCommand(
    string Name,
    ServerBranch Branch,
    int GamePort,
    int RconPort);
