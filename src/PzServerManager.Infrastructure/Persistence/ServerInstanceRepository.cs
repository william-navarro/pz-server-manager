using Microsoft.EntityFrameworkCore;
using PzServerManager.Application.Abstractions;
using PzServerManager.Domain.Servers;

namespace PzServerManager.Infrastructure.Persistence;

internal sealed class ServerInstanceRepository(
    IDbContextFactory<ManagerDbContext> contextFactory) : IServerInstanceRepository
{
    public async Task<IReadOnlyList<ServerInstance>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ServerInstances
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(
        ServerInstance instance,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.ServerInstances.Add(instance);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ServerInstance?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ServerInstances
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task UpdateAsync(
        ServerInstance instance,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.ServerInstances.Update(instance);
        await context.SaveChangesAsync(cancellationToken);
    }
}
