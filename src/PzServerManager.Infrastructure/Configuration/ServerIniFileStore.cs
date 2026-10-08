using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Configuration;

namespace PzServerManager.Infrastructure.Configuration;

public sealed class ServerIniFileStore : IServerIniStore
{
    public async Task<ServerIniDocument> LoadAsync(
        string path,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var text = await File.ReadAllTextAsync(Path.GetFullPath(path), AtomicTextFileStore.Utf8WithoutBom, ct);
        return ServerIniDocument.Parse(text);
    }

    public async Task SaveAsync(
        string path,
        ServerIniDocument document,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        await AtomicTextFileStore.WriteAsync(path, document.ToText(), ct);
    }
}
