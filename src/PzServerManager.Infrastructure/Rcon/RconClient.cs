using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using PzServerManager.Application.Abstractions;

namespace PzServerManager.Infrastructure.Rcon;

public sealed record RconClientTimeouts(TimeSpan Connect, TimeSpan Response, TimeSpan Quiet)
{
    public static RconClientTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(300));
}

public sealed class RconClient : IRconClient, IDisposable
{
    private const int AuthenticationType = 3;
    private const int AuthenticationResponseType = 2;
    private const int CommandType = 2;
    private const int CommandResponseType = 0;
    private const int MaximumResponseBytes = 256 * 1024;
    private readonly IRconSettingsProvider _settingsProvider;
    private readonly RconClientTimeouts _timeouts;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private int _requestId;

    public RconClient(IRconSettingsProvider settingsProvider, RconClientTimeouts? timeouts = null)
    {
        _settingsProvider = settingsProvider;
        _timeouts = timeouts ?? RconClientTimeouts.Default;
    }

    public async Task<bool> CanAuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsProvider.GetAsync(cancellationToken);
        if (settings is null) return false;
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            try
            {
                using var client = await ConnectAsync(settings, cancellationToken);
                return await AuthenticateAsync(client.GetStream(), settings.Password, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or SocketException or TimeoutException or RconAuthenticationException)
            {
                return false;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (command.Any(char.IsControl)) throw new ArgumentException("O comando RCON não pode conter caracteres de controle.", nameof(command));
        var settings = await _settingsProvider.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("O RCON não está configurado no arquivo do servidor.");

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            Exception? lastTransportError = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var client = await ConnectAsync(settings, cancellationToken);
                    var stream = client.GetStream();
                    if (!await AuthenticateAsync(stream, settings.Password, cancellationToken))
                    {
                        throw new RconAuthenticationException("A autenticação RCON foi recusada. Confira a configuração do servidor.");
                    }
                    return await ExecuteCommandAsync(stream, command, cancellationToken);
                }
                catch (Exception exception) when ((exception is IOException or SocketException) && attempt == 0)
                {
                    lastTransportError = exception;
                }
            }

            throw new IOException("A conexão RCON caiu e não foi possível reconectar.", lastTransportError);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task<TcpClient> ConnectAsync(RconConnectionSettings settings, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = new CancellationTokenSource(_timeouts.Connect);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await client.ConnectAsync(settings.Host, settings.Port, linked.Token);
                return client;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("A conexão RCON excedeu o limite de tempo.");
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<bool> AuthenticateAsync(NetworkStream stream, string password, CancellationToken cancellationToken)
    {
        var id = NextRequestId();
        await WritePacketAsync(stream, id, AuthenticationType, password, cancellationToken);
        using var timeout = new CancellationTokenSource(_timeouts.Response);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            for (var packetNumber = 0; packetNumber < 4; packetNumber++)
            {
                var packet = await ReadPacketAsync(stream, linked.Token);
                if (packet.Id == -1) return false;
                if (packet.Type == AuthenticationResponseType) return packet.Id == id;
            }
            throw new IOException("O servidor RCON não enviou uma resposta de autenticação válida.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("A autenticação RCON excedeu o limite de tempo.");
        }
    }

    private async Task<string> ExecuteCommandAsync(NetworkStream stream, string command, CancellationToken cancellationToken)
    {
        var id = NextRequestId();
        await WritePacketAsync(stream, id, CommandType, command, cancellationToken);
        using var responseTimeout = new CancellationTokenSource(_timeouts.Response);
        using var responseLinked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, responseTimeout.Token);
        var result = new StringBuilder();
        var totalBytes = 0;
        var receivedPacket = false;

        while (true)
        {
            using var quietTimeout = receivedPacket ? new CancellationTokenSource(_timeouts.Quiet) : null;
            using var readLinked = quietTimeout is null
                ? CancellationTokenSource.CreateLinkedTokenSource(responseLinked.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(responseLinked.Token, quietTimeout.Token);
            RconPacket packet;
            try
            {
                packet = await ReadPacketAsync(stream, readLinked.Token);
            }
            catch (OperationCanceledException) when (receivedPacket && quietTimeout!.IsCancellationRequested && !responseTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (responseTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("A resposta RCON excedeu o limite de tempo.");
            }

            if (packet.Id != id || packet.Type != CommandResponseType) continue;
            receivedPacket = true;
            totalBytes += Encoding.UTF8.GetByteCount(packet.Body) + 14;
            if (totalBytes > MaximumResponseBytes) throw new IOException("A resposta RCON excedeu o limite de 256 KB.");
            result.Append(packet.Body);
        }

        return result.ToString();
    }

    private static async Task WritePacketAsync(NetworkStream stream, int id, int type, string body, CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var payloadLength = 4 + 4 + bodyBytes.Length + 2;
        var packet = new byte[4 + payloadLength];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), payloadLength);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4, 4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), type);
        bodyBytes.CopyTo(packet.AsSpan(12));
        await stream.WriteAsync(packet, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<RconPacket> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is < 10 or > MaximumResponseBytes) throw new IOException("O servidor RCON enviou um pacote com tamanho inválido.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        if (payload[^1] != 0 || payload[^2] != 0) throw new IOException("O servidor RCON enviou um pacote sem terminador válido.");
        var id = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4));
        var type = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4));
        var body = Encoding.UTF8.GetString(payload, 8, length - 10);
        return new(id, type, body);
    }

    private int NextRequestId()
    {
        var id = Interlocked.Increment(ref _requestId);
        if (id <= 0)
        {
            Interlocked.Exchange(ref _requestId, 1);
            return 1;
        }
        return id;
    }

    public void Dispose() => _connectionLock.Dispose();
    private sealed record RconPacket(int Id, int Type, string Body);
}

public sealed class RconAuthenticationException(string message) : InvalidOperationException(message);
