using PzServerManager.Application.Abstractions;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Infrastructure.Updates;

namespace PzServerManager.Tests;

public sealed class SystemdUpdateRunnerTests
{
    [Fact]
    public async Task RunAsync_StartsPollsAndReturnsJournal()
    {
        var runner = new QueueRunner(
            new(0, "", ""),
            new(0, "ActiveState=activating\nResult=success", ""),
            new(0, "ActiveState=inactive\nResult=success", ""),
            new(0, "backup criado: /tmp/backup.tar.gz", ""));
        var service = new SystemdUpdateRunner(runner, new ServerOperationLock(), new ImmediateDelay());

        var result = await service.RunAsync();

        Assert.True(result.Succeeded);
        Assert.Contains("backup criado", result.Journal);
        Assert.Equal(["start", "--no-block", "pz-atualizar.service"], runner.Calls[0]);
        Assert.Equal(["show", "pz-atualizar.service", "--property=ActiveState,Result"], runner.Calls[1]);
        Assert.Equal(["-u", "pz-atualizar.service", "-n", "50", "--no-pager", "--output=short-iso"], runner.Calls[3]);
    }

    [Fact]
    public async Task RunAsync_IsRejectedWhileAnotherOperationOwnsLock()
    {
        var operationLock = new ServerOperationLock();
        using var lease = operationLock.TryAcquire("backup");
        var runner = new QueueRunner();
        var service = new SystemdUpdateRunner(runner, operationLock, new ImmediateDelay());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync());

        Assert.Contains("backup", exception.Message);
        Assert.Empty(runner.Calls);
    }

    private sealed class ImmediateDelay : IOperationDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class QueueRunner(params CommandResult[] results) : ICommandRunner
    {
        private readonly Queue<CommandResult> _results = new(results);
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        { Calls.Add(arguments.ToArray()); return Task.FromResult(_results.Dequeue()); }
    }
}
