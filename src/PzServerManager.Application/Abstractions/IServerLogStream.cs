namespace PzServerManager.Application.Abstractions;

public interface IServerLogStream
{
    IAsyncEnumerable<string> FollowAsync(
        int initialLineCount = 300,
        CancellationToken cancellationToken = default);
}
