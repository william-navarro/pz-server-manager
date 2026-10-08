using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Servers;
using PzServerManager.Domain.Servers;

namespace PzServerManager.Tests;

public sealed class ServerInstallationServiceTests
{
    [Fact]
    public async Task InstallOrUpdateAsync_TransitionsInstanceToStopped()
    {
        var instance = CreateInstance();
        var repository = new InstallationRepository(instance);
        var steamCmd = new SuccessfulSteamCmdClient();
        var service = new ServerInstallationService(repository, steamCmd);

        await service.InstallOrUpdateAsync(instance.Id);

        Assert.Equal(ServerInstanceStatus.Stopped, instance.Status);
        Assert.True(steamCmd.EnsureCalled);
        Assert.Equal(instance.InstallationPath, steamCmd.InstallationPath);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_MarksInstanceAsFaultedWhenSteamCmdFails()
    {
        var instance = CreateInstance();
        var repository = new InstallationRepository(instance);
        var service = new ServerInstallationService(repository, new FailingSteamCmdClient());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.InstallOrUpdateAsync(instance.Id));

        Assert.Equal(ServerInstanceStatus.Faulted, instance.Status);
    }

    private static ServerInstance CreateInstance() => ServerInstance.Create(
        "Servidor",
        "servidor",
        Path.GetFullPath("server"),
        Path.GetFullPath("data"),
        ServerBranch.Stable,
        16261,
        27015);

    private sealed class InstallationRepository(ServerInstance instance) : IServerInstanceRepository
    {
        public Task<IReadOnlyList<ServerInstance>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServerInstance>>([instance]);

        public Task<ServerInstance?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == instance.Id ? instance : null);

        public Task AddAsync(ServerInstance newInstance, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(ServerInstance updatedInstance, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SuccessfulSteamCmdClient : ISteamCmdClient
    {
        public bool EnsureCalled { get; private set; }
        public string? InstallationPath { get; private set; }

        public SteamCmdStatus GetStatus() => new(true, "steamcmd.exe");

        public Task EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            EnsureCalled = true;
            return Task.CompletedTask;
        }

        public Task InstallOrUpdateServerAsync(
            string installationPath,
            ServerBranch branch,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallationPath = installationPath;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSteamCmdClient : ISteamCmdClient
    {
        public SteamCmdStatus GetStatus() => new(false, "steamcmd.exe");

        public Task EnsureInstalledAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Falha simulada.");

        public Task InstallOrUpdateServerAsync(
            string installationPath,
            ServerBranch branch,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
