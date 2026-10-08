using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Servers;
using PzServerManager.Domain.Servers;

namespace PzServerManager.Tests;

public sealed class ServerInstanceServiceTests
{
    [Fact]
    public async Task CreateAsync_GeneratesSlugAndPersistsInstance()
    {
        var repository = new MemoryRepository();
        var service = new ServerInstanceService(repository, new TestPaths());

        var result = await service.CreateAsync(new CreateServerInstanceCommand(
            "Servidor São Paulo",
            ServerBranch.Stable,
            16261,
            27015));

        Assert.Equal("servidor-sao-paulo", result.Slug);
        Assert.Single(repository.Items);
        Assert.EndsWith(Path.Combine("servidor-sao-paulo", "server"), result.InstallationPath);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateReservedPort()
    {
        var repository = new MemoryRepository();
        var service = new ServerInstanceService(repository, new TestPaths());

        await service.CreateAsync(new CreateServerInstanceCommand(
            "Primeiro",
            ServerBranch.Stable,
            16261,
            27015));

        var exception = await Assert.ThrowsAsync<ServerInstanceValidationException>(() =>
            service.CreateAsync(new CreateServerInstanceCommand(
                "Segundo",
                ServerBranch.Stable,
                27015,
                27016)));

        Assert.Contains("27015", exception.Message);
    }

    private sealed class MemoryRepository : IServerInstanceRepository
    {
        public List<ServerInstance> Items { get; } = [];

        public Task<IReadOnlyList<ServerInstance>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServerInstance>>(Items.ToArray());

        public Task<ServerInstance?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task AddAsync(ServerInstance instance, CancellationToken cancellationToken = default)
        {
            Items.Add(instance);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ServerInstance instance, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestPaths : IManagerPaths
    {
        public string DataRoot { get; } = Path.GetFullPath(".test-data");
        public string DatabasePath => Path.Combine(DataRoot, "data", "manager.db");
        public string InstancesRoot => Path.Combine(DataRoot, "instances");
        public string SteamCmdRoot => Path.Combine(DataRoot, "steamcmd");
        public string BackupsRoot => Path.Combine(DataRoot, "backups");
        public string LogsRoot => Path.Combine(DataRoot, "logs");
    }
}
