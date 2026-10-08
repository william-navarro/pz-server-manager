using Microsoft.EntityFrameworkCore;
using PzServerManager.Application.Abstractions;
using PzServerManager.Infrastructure.Persistence;

namespace PzServerManager.Infrastructure;

internal sealed class InfrastructureInitializer(
    IManagerPaths paths,
    IDbContextFactory<ManagerDbContext> contextFactory) : IInfrastructureInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.DataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath)!);
        Directory.CreateDirectory(paths.InstancesRoot);
        Directory.CreateDirectory(paths.SteamCmdRoot);
        Directory.CreateDirectory(paths.BackupsRoot);
        Directory.CreateDirectory(paths.LogsRoot);
        Directory.CreateDirectory(Path.Combine(paths.DataRoot, "cardinal"));

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.EnsureCreatedAsync(cancellationToken);
    }
}
