using PzServerManager.Application.Abstractions;

namespace PzServerManager.Application.Servers;

public sealed class ServerInstallationService(
    IServerInstanceRepository repository,
    ISteamCmdClient steamCmdClient)
{
    public SteamCmdStatus GetSteamCmdStatus() => steamCmdClient.GetStatus();

    public async Task InstallOrUpdateAsync(
        Guid instanceId,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var instance = await repository.GetAsync(instanceId, cancellationToken)
            ?? throw new ServerInstanceValidationException("A instância selecionada não existe.");

        instance.MarkInstalling();
        await repository.UpdateAsync(instance, cancellationToken);

        try
        {
            progress?.Report("Preparando o SteamCMD...");
            await steamCmdClient.EnsureInstalledAsync(progress, cancellationToken);

            progress?.Report($"Instalando Project Zomboid Dedicated Server ({instance.Branch})...");
            await steamCmdClient.InstallOrUpdateServerAsync(
                instance.InstallationPath,
                instance.Branch,
                progress,
                cancellationToken);

            instance.MarkStopped();
            await repository.UpdateAsync(instance, cancellationToken);
            progress?.Report("Instalação validada com sucesso.");
        }
        catch
        {
            instance.MarkFaulted();
            await repository.UpdateAsync(instance, CancellationToken.None);
            throw;
        }
    }
}
