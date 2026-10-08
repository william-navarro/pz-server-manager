using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Backups;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Infrastructure.Configuration;
using PzServerManager.Infrastructure.ManagedServer;

namespace PzServerManager.Tests;

public sealed class SystemdBackupManagerTests
{
    [Fact]
    public async Task GetOverviewAsync_UsesExactCommandsAndSortsFiles()
    {
        using var directory = new TemporaryDirectory();
        CreateBackup(directory.Path, "servertest-20260101-010101-manual.tar.gz", 10);
        CreateBackup(directory.Path, "servertest-20261231-235959-diario.tar.gz", 20);
        File.WriteAllText(Path.Combine(directory.Path, "ignore.txt"), "x");
        var runner = new QueueRunner(
            new(0, "NextElapseUSecRealtime=Sun 2026-10-04 05:00:00 -03\n", ""),
            new(0, "Result=success\nExecMainExitTimestamp=Sat 2026-10-03 12:00:00 -03\n\nResult=success\nExecMainExitTimestamp=Sat 2026-10-03 05:00:00 -03\n", ""));
        var manager = CreateManager(directory.Path, runner);

        var overview = await manager.GetOverviewAsync();

        Assert.Equal(2, overview.Files.Count);
        Assert.Equal("servertest-20261231-235959-diario.tar.gz", overview.Files[0].FileName);
        Assert.Collection(runner.Calls,
            call => Assert.Equal(["show", "pz-backup-diario.timer", "--property=NextElapseUSecRealtime"], call.Arguments),
            call => Assert.Equal(["show", "pz-backup.service", "pz-backup-diario.service", "--property=Result,ExecMainExitTimestamp"], call.Arguments));
        Assert.All(runner.Calls, call => Assert.Equal("systemctl", call.FileName));
    }

    [Fact]
    public async Task CreateBackupAsync_StartsWithoutBlockingPollsAndReadsJournal()
    {
        using var directory = new TemporaryDirectory();
        var runner = new QueueRunner(
            new(0, "", ""),
            new(0, "ActiveState=activating\nResult=success\n", ""),
            new(0, "ActiveState=inactive\nResult=success\n", ""),
            new(0, "backup complete", ""));
        var audit = new RecordingAudit();
        var manager = CreateManager(directory.Path, runner, audit);

        var result = await manager.CreateBackupAsync("admin");

        Assert.True(result.Succeeded);
        Assert.Equal("backup complete", result.Journal);
        Assert.Equal(["start", "--no-block", "pz-backup.service"], runner.Calls[0].Arguments);
        Assert.Equal(["show", "pz-backup.service", "--property=ActiveState,Result"], runner.Calls[1].Arguments);
        Assert.Equal(["show", "pz-backup.service", "--property=ActiveState,Result"], runner.Calls[2].Arguments);
        Assert.Equal("journalctl", runner.Calls[3].FileName);
        Assert.Equal(["-u", "pz-backup.service", "-n", "30", "--no-pager", "--output=short-iso"], runner.Calls[3].Arguments);
        Assert.Contains(audit.Entries, entry => entry.Action == "fazer backup manual" && entry.Result == "sucesso");
    }

    [Fact]
    public async Task CreateBackupAsync_WithReason_UsesValidatedTemplateUnit()
    {
        using var directory = new TemporaryDirectory();
        var runner = new QueueRunner(new(0, "", ""), new(0, "ActiveState=inactive\nResult=success\n", ""), new(0, "backup criado", ""));
        var manager = CreateManager(directory.Path, runner);

        var result = await manager.CreateBackupAsync("automação", "atualizacao-mods");

        Assert.True(result.Succeeded);
        Assert.Equal(["start", "--no-block", "pz-backup@atualizacao-mods.service"], runner.Calls[0].Arguments);
    }

    [Theory]
    [InlineData("../jogo")]
    [InlineData("ATUALIZACAO")]
    [InlineData("motivo-com-mais-de-vinte")]
    public async Task CreateBackupAsync_WithReason_RejectsUnsafeReason(string reason)
    {
        using var directory = new TemporaryDirectory();
        var runner = new QueueRunner();
        var manager = CreateManager(directory.Path, runner);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.CreateBackupAsync("admin", reason));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task RestoreAsync_UsesValidatedFileNameInExactUnitArgument()
    {
        using var directory = new TemporaryDirectory();
        const string fileName = "servertest-20261003-142530-antes-zerar.tar.gz";
        CreateBackup(directory.Path, fileName, 1);
        var runner = new QueueRunner(
            new(0, "", ""),
            new(0, "ActiveState=inactive\nResult=success\n", ""),
            new(0, "restored", ""));
        var manager = CreateManager(directory.Path, runner);

        var result = await manager.RestoreAsync("admin", fileName);

        Assert.True(result.Succeeded);
        Assert.Equal(["start", "--no-block", $"pz-restaurar@{fileName}.service"], runner.Calls[0].Arguments);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("servertest-20261003-142530-manual.tar.gz")]
    public async Task RestoreAsync_RejectsInvalidOrUnlistedNameBeforeSystemctl(string fileName)
    {
        using var directory = new TemporaryDirectory();
        var runner = new QueueRunner();
        var manager = CreateManager(directory.Path, runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RestoreAsync("admin", fileName));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task ResetWorldAsync_ReturnsFailureFromServiceResult()
    {
        using var directory = new TemporaryDirectory();
        var runner = new QueueRunner(
            new(0, "", ""),
            new(0, "ActiveState=failed\nResult=exit-code\n", ""),
            new(0, "script failed", ""));
        var manager = CreateManager(directory.Path, runner);

        var result = await manager.ResetWorldAsync("admin");

        Assert.False(result.Succeeded);
        Assert.False(result.TimedOut);
        Assert.Contains("exit-code", result.Message);
    }

    [Fact]
    public async Task CreateBackupAsync_TimesOutAfterTenMinutesWorthOfPolls()
    {
        using var directory = new TemporaryDirectory();
        var results = new List<CommandResult> { new(0, "", "") };
        results.AddRange(Enumerable.Repeat(new CommandResult(0, "ActiveState=activating\nResult=success\n", ""), 300));
        results.Add(new CommandResult(0, "still running", ""));
        var runner = new QueueRunner(results.ToArray());
        var manager = CreateManager(directory.Path, runner);

        var result = await manager.CreateBackupAsync("admin");

        Assert.False(result.Succeeded);
        Assert.True(result.TimedOut);
        Assert.Equal(302, runner.Calls.Count);
    }

    [Fact]
    public async Task Operations_AreRejectedWhileSharedLockIsHeld()
    {
        using var directory = new TemporaryDirectory();
        var operationLock = new ServerOperationLock();
        using var lease = operationLock.TryAcquire("restauração de backup");
        var runner = new QueueRunner();
        var manager = CreateManager(directory.Path, runner, operationLock: operationLock);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateBackupAsync("admin"));

        Assert.Contains("restauração de backup", exception.Message);
        Assert.Empty(runner.Calls);
    }

    private static SystemdBackupManager CreateManager(
        string backupPath,
        ICommandRunner runner,
        IAuditLog? audit = null,
        IServerOperationLock? operationLock = null) => new(
            runner,
            Options.Create(new ManagerOptions
            {
                ManagedServer = new ManagedServerOptions
                {
                    DisplayName = "Test server", ServiceName = "pzserver.service", InstallPath = "/opt/pz",
                    DataPath = "/opt/data", ServerName = "servertest", ConsoleFifo = "/run/pz/console",
                    BackupsPath = backupPath
                }
            }),
            audit ?? new RecordingAudit(),
            operationLock ?? new ServerOperationLock(),
            new ImmediateDelay(),
            new BackupFileNameParser("servertest"));

    private static void CreateBackup(string directory, string name, int length) =>
        File.WriteAllBytes(Path.Combine(directory, name), new byte[length]);

    private sealed class ImmediateDelay : IOperationDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class QueueRunner(params CommandResult[] results) : ICommandRunner
    {
        private readonly Queue<CommandResult> _results = new(results);
        public List<Call> Calls { get; } = [];
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(fileName, arguments.ToArray(), timeout));
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class RecordingAudit : IAuditLog
    {
        public List<(string Actor, string Action, string Result)> Entries { get; } = [];
        public Task WriteAsync(string userName, string action, string result, CancellationToken cancellationToken = default)
        {
            Entries.Add((userName, action, result));
            return Task.CompletedTask;
        }
    }

    private sealed record Call(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"PzServerManager.Backups-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
