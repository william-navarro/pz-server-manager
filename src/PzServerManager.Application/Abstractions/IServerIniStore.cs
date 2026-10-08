using PzServerManager.Application.Configuration;

namespace PzServerManager.Application.Abstractions;

public interface IServerIniStore
{
    Task<ServerIniDocument> LoadAsync(string path, CancellationToken ct = default);
    Task SaveAsync(string path, ServerIniDocument document, CancellationToken ct = default);
}
