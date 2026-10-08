using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Configuration;

namespace PzServerManager.Infrastructure.Configuration;

public sealed class SandboxVarsFileStore : ISandboxVarsStore
{
    public async Task<SandboxVarsDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var text = await File.ReadAllTextAsync(
            Path.GetFullPath(path), AtomicTextFileStore.Utf8WithoutBom, cancellationToken);
        return SandboxVarsDocument.Parse(text);
    }

    public async Task SaveAsync(
        string path,
        SandboxVarsDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        await AtomicTextFileStore.WriteAsync(path, document.ToText(), cancellationToken);
    }
}
