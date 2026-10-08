using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Infrastructure.Configuration;
using PzServerManager.Infrastructure.ManagedServer;

namespace PzServerManager.Tests;

public sealed class SystemdServerControllerTests
{
    [Fact]
    public async Task GetStatusAsync_UsesExactSystemctlArgumentsAndMapsStoppedState()
    {
        using var directory = new TemporaryDirectory();
        var runner = new FakeCommandRunner(new CommandResult(0, "ActiveState=inactive\nSubState=dead\nMainPID=0\nNRestarts=2\n", ""));
        var controller = CreateController(runner, directory.Path);

        var status = await controller.GetStatusAsync();

        Assert.Equal(ManagedServerState.Stopped, status.State);
        Assert.Equal(2, status.RestartCount);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("systemctl", call.FileName);
        Assert.Equal(["show", "pzserver.service", "--property=ActiveState,SubState,MainPID,ActiveEnterTimestamp,NRestarts,MemoryCurrent,CPUUsageNSec"], call.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(15), call.Timeout);
    }

    [Fact]
    public async Task GetStatusAsync_UsesSystemdAccountingInsteadOfGameProc()
    {
        using var directory = new TemporaryDirectory();
        var runner = new FakeCommandRunner(new CommandResult(0,
            "ActiveState=active\nSubState=running\nMainPID=4321\nNRestarts=0\nMemoryCurrent=5368709120\nCPUUsageNSec=123456789\n", ""));
        var controller = CreateController(runner, directory.Path);

        var status = await controller.GetStatusAsync();

        Assert.Equal(5368709120, status.ProcessMemoryBytes);
        Assert.Equal(123456789UL, status.CpuUsageNanoseconds);
    }

    [Theory]
    [InlineData("start", 30)]
    [InlineData("stop", 120)]
    [InlineData("restart", 120)]
    public async Task Actions_UseExactArgumentsAndTimeout(string action, int seconds)
    {
        using var directory = new TemporaryDirectory();
        var runner = new FakeCommandRunner(new CommandResult(0, "", ""));
        var controller = CreateController(runner, directory.Path);

        if (action == "start") await controller.StartAsync("admin");
        else if (action == "stop") await controller.StopAsync("admin");
        else await controller.RestartAsync("admin");

        var call = Assert.Single(runner.Calls);
        Assert.Equal([action, "pzserver.service"], call.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(seconds), call.Timeout);
        Assert.Contains($"\tadmin\t{(action == "start" ? "iniciar" : action == "stop" ? "parar" : "reiniciar")}\tsucesso", await File.ReadAllTextAsync(Path.Combine(directory.Path, "audit.log")));
    }

    [Fact]
    public async Task Action_ThrowsFriendlyErrorAndAuditsFailure()
    {
        using var directory = new TemporaryDirectory();
        var runner = new FakeCommandRunner(new CommandResult(1, "", "access denied"));
        var controller = CreateController(runner, directory.Path);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StartAsync("admin"));

        Assert.Contains("access denied", exception.Message);
        Assert.Contains("erro", await File.ReadAllTextAsync(Path.Combine(directory.Path, "audit.log")));
    }

    [Fact]
    public async Task Action_IsRejectedWhileSharedServerOperationIsRunning()
    {
        using var directory = new TemporaryDirectory();
        var runner = new FakeCommandRunner();
        var operationLock = new ServerOperationLock();
        using var lease = operationLock.TryAcquire("backup manual");
        var controller = CreateController(runner, directory.Path, operationLock);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StopAsync("admin"));

        Assert.Contains("backup manual", exception.Message);
        Assert.Empty(runner.Calls);
    }

    private static SystemdServerController CreateController(
        FakeCommandRunner runner,
        string root,
        IServerOperationLock? operationLock = null) => new(
        runner,
        Options.Create(new ManagerOptions
        {
            ManagedServer = new ManagedServerOptions
            {
                DisplayName = "Test server", ServiceName = "pzserver.service", InstallPath = "/opt/pz",
                DataPath = "/opt/data", ServerName = "servertest", ConsoleFifo = "/run/pz/console"
            }
        }),
        new AuditLog(new TestPaths(root)),
        operationLock ?? new ServerOperationLock());

    private sealed class FakeCommandRunner(params CommandResult[] results) : ICommandRunner
    {
        private readonly Queue<CommandResult> _results = new(results);
        public List<Call> Calls { get; } = [];
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Calls.Add(new Call(fileName, arguments.ToArray(), timeout));
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed record Call(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout);
    private sealed class TestPaths(string root) : IManagerPaths
    {
        public string DataRoot => root;
        public string DatabasePath => Path.Combine(root, "data", "manager.db");
        public string InstancesRoot => Path.Combine(root, "instances");
        public string SteamCmdRoot => Path.Combine(root, "steamcmd");
        public string BackupsRoot => Path.Combine(root, "backups");
        public string LogsRoot => Path.Combine(root, "logs");
    }
    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PzServerManager.Systemd-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
