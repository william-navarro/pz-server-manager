using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Automation;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Application.Rcon;

namespace PzServerManager.Tests;

public sealed class ServerRestartCoordinatorTests
{
    [Fact]
    public async Task WarnAndRestartAsync_CanBeCancelledDuringCountdown()
    {
        var controller = new RecordingController();
        var rcon = new SignalingRconClient();
        var audit = new NullAuditLog();
        using var coordinator = new ServerRestartCoordinator(
            controller, new ServerAdministrationService(rcon, audit), audit, TimeProvider.System);

        var operation = coordinator.WarnAndRestartAsync("admin");
        await rcon.CommandSent.Task.WaitAsync(TimeSpan.FromSeconds(1));
        coordinator.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal("servermsg \"O servidor vai reiniciar em 5 minutos\"", Assert.Single(rcon.Commands));
        Assert.False(controller.Restarted);
    }

    [Fact]
    public async Task WarnAndRestartAsync_HoldsSharedOperationLockDuringCountdown()
    {
        var controller = new RecordingController();
        var rcon = new SignalingRconClient();
        var audit = new NullAuditLog();
        var operationLock = new ServerOperationLock();
        using var coordinator = new ServerRestartCoordinator(controller, new ServerAdministrationService(rcon, audit), audit,
            new SettingsStore(), operationLock, TimeProvider.System);

        var operation = coordinator.WarnAndRestartAsync("admin");
        await rcon.CommandSent.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(operationLock.IsBusy);
        Assert.Null(operationLock.TryAcquire("backup"));
        coordinator.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.False(operationLock.IsBusy);
    }

    private sealed class SignalingRconClient : IRconClient
    {
        public List<string> Commands { get; } = [];
        public TaskCompletionSource CommandSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> CanAuthenticateAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command); CommandSent.TrySetResult(); return Task.FromResult(string.Empty);
        }
    }
    private sealed class RecordingController : IServerController
    {
        public bool Restarted { get; private set; }
        public Task<ManagedServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestartAsync(CancellationToken cancellationToken = default) { Restarted = true; return Task.CompletedTask; }
        public Task StartAsync(string userName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StopAsync(string userName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestartAsync(string userName, CancellationToken cancellationToken = default) { Restarted = true; return Task.CompletedTask; }
    }
    private sealed class NullAuditLog : IAuditLog
    {
        public Task WriteAsync(string userName, string action, string result, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class SettingsStore : IAutomationSettingsStore
    {
        public Task<AutomationSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AutomationSettings { WarningSeconds = [5] });
        public Task SaveAsync(AutomationSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
