using PzServerManager.Application.Abstractions;

namespace PzServerManager.Infrastructure.ManagedServer;

public sealed class ServerCommandReadiness(
    IServerController controller,
    IRconSettingsProvider rconSettings,
    IRconClient rconClient,
    TimeProvider timeProvider) : IServerCommandReadiness
{
    private static readonly TimeSpan StartupFallback = TimeSpan.FromSeconds(90);

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var status = await controller.GetStatusAsync(cancellationToken);
        if (!status.ActiveState.Equals("active", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("O servidor está parado. O comando não foi enviado.");
        }

        var settings = await rconSettings.GetAsync(cancellationToken);
        if (settings is not null)
        {
            if (!await rconClient.CanAuthenticateAsync(cancellationToken))
            {
                throw new InvalidOperationException("O servidor ainda está iniciando e o RCON não está pronto. O comando não foi enviado.");
            }
            return;
        }

        var activeFor = status.ActiveSince is null
            ? TimeSpan.Zero
            : timeProvider.GetUtcNow() - status.ActiveSince.Value.ToUniversalTime();
        if (activeFor < StartupFallback)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((StartupFallback - activeFor).TotalSeconds));
            throw new InvalidOperationException($"O servidor ainda está iniciando. Aguarde cerca de {seconds} segundo(s); o comando não foi enviado.");
        }
    }
}
