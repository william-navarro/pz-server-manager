namespace PzServerManager.Application.Abstractions;

public interface IServerConsole
{
    Task SendAsync(string command, CancellationToken cancellationToken = default);
}
