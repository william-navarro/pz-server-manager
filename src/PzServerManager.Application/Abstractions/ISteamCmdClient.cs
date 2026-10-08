using PzServerManager.Domain.Servers;

namespace PzServerManager.Application.Abstractions;

public interface ISteamCmdClient
{
    SteamCmdStatus GetStatus();

    Task EnsureInstalledAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task InstallOrUpdateServerAsync(
        string installationPath,
        ServerBranch branch,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record SteamCmdStatus(bool IsInstalled, string ExecutablePath);
