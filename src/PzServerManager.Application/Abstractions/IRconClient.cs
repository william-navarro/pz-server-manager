namespace PzServerManager.Application.Abstractions;

public interface IRconClient
{
    Task<bool> CanAuthenticateAsync(CancellationToken cancellationToken = default);
    Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default);
}
