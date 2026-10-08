namespace PzServerManager.Application.Abstractions;

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}
