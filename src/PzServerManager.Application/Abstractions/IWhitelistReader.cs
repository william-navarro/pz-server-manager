using PzServerManager.Application.Whitelist;

namespace PzServerManager.Application.Abstractions;

public interface IWhitelistReader
{
    Task<IReadOnlyList<WhitelistAccount>> ListAsync(CancellationToken cancellationToken = default);
}
