using PzServerManager.Application.Configuration;

namespace PzServerManager.Application.Abstractions;

public interface ISandboxVarsStore
{
    Task<SandboxVarsDocument> LoadAsync(string path, CancellationToken cancellationToken = default);
    Task SaveAsync(string path, SandboxVarsDocument document, CancellationToken cancellationToken = default);
}
