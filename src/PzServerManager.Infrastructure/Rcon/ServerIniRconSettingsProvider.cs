using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.Rcon;

public sealed class ServerIniRconSettingsProvider(
    IServerIniStore iniStore,
    IOptions<ManagerOptions> options) : IRconSettingsProvider
{
    public async Task<RconConnectionSettings?> GetAsync(CancellationToken cancellationToken = default)
    {
        var managed = options.Value.ManagedServer
            ?? throw new InvalidOperationException("O servidor Linux gerenciado não foi configurado.");
        var document = await iniStore.LoadAsync(managed.ServerIniPath, cancellationToken);
        var password = document.Get("RCONPassword");
        if (string.IsNullOrEmpty(password)) return null;
        if (!int.TryParse(document.Get("RCONPort"), out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("RCONPort não contém uma porta válida.");
        }

        return new("127.0.0.1", port, password);
    }
}
