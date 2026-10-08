using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.ManagedServer;

public sealed class JournalctlServerLogStream(IOptions<ManagerOptions> options) : IServerLogStream
{
    private readonly ManagedServerOptions _options = options.Value.ManagedServer
        ?? throw new InvalidOperationException("O servidor Linux gerenciado não foi configurado.");

    public async IAsyncEnumerable<string> FollowAsync(
        int initialLineCount = 300,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (initialLineCount is < 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(initialLineCount));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "journalctl",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-u", _options.ServiceName, "-o", "cat", "-n", initialLineCount.ToString(), "-f" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Não foi possível iniciar a leitura do journal.");
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                yield return line;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await process.WaitForExitAsync(CancellationToken.None);
                var error = await process.StandardError.ReadToEndAsync(CancellationToken.None);
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error)
                            ? $"journalctl terminou com o código {process.ExitCode}."
                            : $"journalctl: {error.Trim()}");
                }
            }
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
