using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Infrastructure.Configuration;
using PzServerManager.Infrastructure.ManagedServer;

namespace PzServerManager.Tests;

public sealed class ServerCommandReadinessTests
{
    [Fact]
    public async Task StoppedServer_IsRejectedWithoutContactingRcon()
    {
        var rcon = new FakeRconClient();
        var settings = new FakeSettingsProvider(new("127.0.0.1", 27015, "segredo"));
        var readiness = new ServerCommandReadiness(
            new FakeController(Status("inactive", DateTimeOffset.UtcNow)), settings, rcon, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => readiness.EnsureReadyAsync());
        Assert.Contains("parado", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(settings.Called);
        Assert.False(rcon.Called);
    }

    [Fact]
    public async Task FifoConsole_DoesNotReachOperatingSystemWhenReadinessRejectsStoppedServer()
    {
        var console = new FifoServerConsole(
            Options.Create(new ManagerOptions { ManagedServer = new ManagedServerOptions { ConsoleFifo = "/arquivo-que-nao-deve-ser-aberto" } }),
            new RejectingReadiness());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => console.SendAsync("quit"));
        Assert.Contains("não foi enviado", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ActiveForNinetySeconds_IsReadyWithoutRcon()
    {
        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        var readiness = new ServerCommandReadiness(
            new FakeController(Status("active", now.AddSeconds(-91))),
            new FakeSettingsProvider(null), new FakeRconClient(), new FixedTimeProvider(now));
        await readiness.EnsureReadyAsync();
    }

    [Fact]
    public async Task ConfiguredRcon_MustAuthenticateEvenAfterNinetySeconds()
    {
        var now = DateTimeOffset.UtcNow;
        var rcon = new FakeRconClient { Result = false };
        var readiness = new ServerCommandReadiness(
            new FakeController(Status("active", now.AddMinutes(-10))),
            new FakeSettingsProvider(new("127.0.0.1", 27015, "segredo")), rcon, new FixedTimeProvider(now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => readiness.EnsureReadyAsync());
        Assert.True(rcon.Called);
    }

    private static ManagedServerStatus Status(string activeState, DateTimeOffset activeSince) => new(
        activeState == "active" ? ManagedServerState.Running : ManagedServerState.Stopped,
        activeState, activeState == "active" ? "running" : "dead", null, activeSince, 0, null, null, null);

    private sealed class FakeController(ManagedServerStatus status) : IServerController
    {
        public Task<ManagedServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(status);
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartAsync(string userName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(string userName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartAsync(string userName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class FakeSettingsProvider(RconConnectionSettings? result) : IRconSettingsProvider
    {
        public bool Called { get; private set; }
        public Task<RconConnectionSettings?> GetAsync(CancellationToken cancellationToken = default) { Called = true; return Task.FromResult(result); }
    }
    private sealed class FakeRconClient : IRconClient
    {
        public bool Called { get; private set; }
        public bool Result { get; set; } = true;
        public Task<bool> CanAuthenticateAsync(CancellationToken cancellationToken = default) { Called = true; return Task.FromResult(Result); }
        public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class RejectingReadiness : IServerCommandReadiness
    {
        public Task EnsureReadyAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("O servidor está parado. O comando não foi enviado.");
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
