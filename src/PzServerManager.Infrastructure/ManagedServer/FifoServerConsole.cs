using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Win32.SafeHandles;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.ManagedServer;

public sealed class FifoServerConsole(
    IOptions<ManagerOptions> options,
    IServerCommandReadiness readiness) : IServerConsole
{
    private const int WriteOnly = 1;
    private const int NonBlocking = 0x800;
    private const int NoReaderError = 6;
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);
    private readonly string _fifoPath = options.Value.ManagedServer?.ConsoleFifo
        ?? throw new InvalidOperationException("O servidor Linux gerenciado não foi configurado.");

    public async Task SendAsync(string command, CancellationToken cancellationToken = default)
    {
        var normalized = ServerConsoleCommand.Validate(command);
        await readiness.EnsureReadyAsync(cancellationToken);
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("O console FIFO está disponível somente no Linux.");
        }

        var descriptor = Open(_fifoPath, WriteOnly | NonBlocking);
        if (descriptor < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error == NoReaderError)
            {
                throw new InvalidOperationException("O servidor está parado ou ainda não abriu o console.");
            }

            throw new IOException($"Não foi possível abrir o console do servidor: {new Win32Exception(error).Message}");
        }

        await using var stream = new FileStream(
            new SafeFileHandle((IntPtr)descriptor, ownsHandle: true),
            FileAccess.Write,
            bufferSize: 4096,
            isAsync: true);
        using var timeoutSource = new CancellationTokenSource(WriteTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes($"{normalized}\n"), linkedSource.Token);
            await stream.FlushAsync(linkedSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("O servidor não respondeu ao console em até 2 segundos.");
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("O servidor está parado ou não está aceitando comandos.", exception);
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int Open(string path, int flags);
}
