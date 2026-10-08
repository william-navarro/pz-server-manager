using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Mods;

namespace PzServerManager.Infrastructure.Steam;

public sealed class SteamWorkshopDownloader(IManagerPaths paths, ICommandRunner commandRunner) : IWorkshopDownloader
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    public async Task DownloadAsync(
        IReadOnlyList<string> itemIds,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (itemIds.Count == 0) throw new ArgumentException("Informe ao menos um item da Workshop.", nameof(itemIds));
        var normalized = itemIds.Select(WorkshopIdParser.Parse).ToArray();
        ModDependencyResolver.EnsureUnique(normalized, "Item da Workshop");

        var executable = Path.Combine(paths.DataRoot, "steamcmd", "steamcmd.sh");
        var workshopRoot = Path.Combine(paths.DataRoot, "workshop");
        var arguments = new List<string>
        {
            "+force_install_dir", workshopRoot,
            "+login", "anonymous"
        };
        foreach (var id in normalized)
        {
            arguments.AddRange(["+workshop_download_item", "108600", id]);
        }
        arguments.Add("+quit");

        progress?.Report($"Baixando {normalized.Length} item(ns) da Workshop...");
        var result = await commandRunner.RunAsync(executable, arguments, DownloadTimeout, cancellationToken);
        ReportLines(result.StandardOutput, progress);
        ReportLines(result.StandardError, progress);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"SteamCMD terminou com o código {result.ExitCode}.");
        }
        progress?.Report("Download da Workshop concluído.");
    }

    private static void ReportLines(string text, IProgress<string>? progress)
    {
        if (progress is null) return;
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            progress.Report(line);
        }
    }
}
