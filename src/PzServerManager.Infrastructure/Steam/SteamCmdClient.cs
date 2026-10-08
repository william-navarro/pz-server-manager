using System.Diagnostics;
using System.IO.Compression;
using PzServerManager.Application.Abstractions;
using PzServerManager.Domain.Servers;

namespace PzServerManager.Infrastructure.Steam;

internal sealed class SteamCmdClient(IManagerPaths paths) : ISteamCmdClient
{
    private const string DownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
    private const string DedicatedServerAppId = "380870";
    private static readonly HttpClient HttpClient = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public SteamCmdStatus GetStatus() => new(File.Exists(ExecutablePath), ExecutablePath);

    public async Task EnsureInstalledAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(ExecutablePath))
        {
            progress?.Report("SteamCMD já está instalado.");
            return;
        }

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(ExecutablePath))
            {
                progress?.Report("SteamCMD já está instalado.");
                return;
            }

            Directory.CreateDirectory(paths.SteamCmdRoot);
            var archivePath = Path.Combine(paths.SteamCmdRoot, "steamcmd.zip.download");

            progress?.Report("Baixando SteamCMD do servidor oficial da Valve...");
            using var response = await HttpClient.GetAsync(
                DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var destination = new FileStream(
                archivePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                await response.Content.CopyToAsync(destination, cancellationToken);
            }

            progress?.Report("Extraindo SteamCMD...");
            ZipFile.ExtractToDirectory(archivePath, paths.SteamCmdRoot, overwriteFiles: true);
            File.Delete(archivePath);

            if (!File.Exists(ExecutablePath))
            {
                throw new InvalidOperationException("O pacote baixado não contém steamcmd.exe.");
            }

            progress?.Report("Inicializando SteamCMD pela primeira vez...");
            await RunSteamCmdAsync(["+quit"], progress, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task InstallOrUpdateServerAsync(
        string installationPath,
        ServerBranch branch,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ExecutablePath))
        {
            throw new InvalidOperationException("SteamCMD ainda não foi instalado.");
        }

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(installationPath);

            var arguments = new List<string>
            {
                "+force_install_dir", installationPath,
                "+login", "anonymous",
                "+app_update", DedicatedServerAppId
            };

            switch (branch)
            {
                case ServerBranch.Unstable:
                    arguments.AddRange(["-beta", "unstable"]);
                    break;
                case ServerBranch.Legacy41:
                    arguments.AddRange(["-beta", "legacy41"]);
                    break;
            }

            arguments.Add("validate");
            arguments.Add("+quit");

            await RunSteamCmdAsync(arguments, progress, cancellationToken);

            var launcher = Path.Combine(installationPath, "StartServer64.bat");
            if (!File.Exists(launcher))
            {
                throw new InvalidOperationException(
                    "O SteamCMD terminou, mas StartServer64.bat não foi encontrado. Verifique o log da instalação.");
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private string ExecutablePath => Path.Combine(paths.SteamCmdRoot, "steamcmd.exe");

    private async Task RunSteamCmdAsync(
        IEnumerable<string> arguments,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = paths.SteamCmdRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        });

        var outputTask = PumpOutputAsync(process.StandardOutput, progress, cancellationToken);
        var errorTask = PumpOutputAsync(process.StandardError, progress, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(outputTask, errorTask);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"SteamCMD terminou com o código {process.ExitCode}.");
        }
    }

    private static async Task PumpOutputAsync(
        StreamReader reader,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line))
            {
                progress?.Report(line);
            }
        }
    }
}
