using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PzServerManager.Application.Abstractions;
using PzServerManager.Infrastructure.Rcon;

namespace PzServerManager.Tests;

public sealed class RconClientTests
{
    [Fact]
    public async Task CanAuthenticateAsync_AcceptsMatchingAuthenticationResponse()
    {
        await using var server = new FakeRconServer(async stream =>
        {
            var auth = await ReadPacketAsync(stream);
            Assert.Equal(3, auth.Type);
            Assert.Equal("segredo", auth.Body);
            await WritePacketAsync(stream, auth.Id, 2, string.Empty);
        });
        using var client = CreateClient(server.Port);
        Assert.True(await client.CanAuthenticateAsync());
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsFriendlyErrorForWrongPassword()
    {
        await using var server = new FakeRconServer(async stream =>
        {
            _ = await ReadPacketAsync(stream);
            await WritePacketAsync(stream, -1, 2, string.Empty);
        });
        using var client = CreateClient(server.Port);
        var exception = await Assert.ThrowsAsync<RconAuthenticationException>(() => client.ExecuteAsync("players"));
        Assert.DoesNotContain("segredo", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ConcatenatesTwoRealSizedPackets()
    {
        var help = RconParsingTests.FixtureSection("help");
        var first = help[..4086];
        var second = help[4086..];
        await using var server = new FakeRconServer(async stream =>
        {
            var auth = await ReadPacketAsync(stream);
            await WritePacketAsync(stream, auth.Id, 2, string.Empty);
            var command = await ReadPacketAsync(stream);
            Assert.Equal(2, command.Type);
            Assert.Equal("help", command.Body);
            await WritePacketAsync(stream, command.Id, 0, first);
            await WritePacketAsync(stream, command.Id, 0, second);
            await Task.Delay(100);
        });
        using var client = CreateClient(server.Port);
        Assert.Equal(help, await client.ExecuteAsync("help"));
    }

    [Fact]
    public async Task ExecuteAsync_TimesOutWhenCommandHasNoResponse()
    {
        await using var server = new FakeRconServer(async stream =>
        {
            var auth = await ReadPacketAsync(stream);
            await WritePacketAsync(stream, auth.Id, 2, string.Empty);
            _ = await ReadPacketAsync(stream);
            await Task.Delay(300);
        });
        using var client = CreateClient(server.Port, responseMilliseconds: 60);
        await Assert.ThrowsAsync<TimeoutException>(() => client.ExecuteAsync("players"));
    }

    private static RconClient CreateClient(int port, int responseMilliseconds = 500) => new(
        new FixedSettingsProvider(new("127.0.0.1", port, "segredo")),
        new(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(responseMilliseconds), TimeSpan.FromMilliseconds(25)));

    private static async Task<Packet> ReadPacketAsync(NetworkStream stream)
    {
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload);
        return new(
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(0, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4, 4)),
            Encoding.UTF8.GetString(payload, 8, length - 10));
    }

    private static async Task WritePacketAsync(NetworkStream stream, int id, int type, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var length = bodyBytes.Length + 10;
        var bytes = new byte[length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), id);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), type);
        bodyBytes.CopyTo(bytes.AsSpan(12));
        await stream.WriteAsync(bytes);
    }

    private sealed class FixedSettingsProvider(RconConnectionSettings settings) : IRconSettingsProvider
    {
        public Task<RconConnectionSettings?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<RconConnectionSettings?>(settings);
    }

    private sealed class FakeRconServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serverTask;
        public FakeRconServer(Func<NetworkStream, Task> handler)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serverTask = Task.Run(async () =>
            {
                using var socket = await _listener.AcceptTcpClientAsync();
                await handler(socket.GetStream());
            });
        }
        public int Port { get; }
        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try { await _serverTask; } catch (SocketException) { }
        }
    }
    private sealed record Packet(int Id, int Type, string Body);
}
