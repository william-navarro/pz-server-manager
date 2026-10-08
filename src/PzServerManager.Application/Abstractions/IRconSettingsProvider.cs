namespace PzServerManager.Application.Abstractions;

public interface IRconSettingsProvider
{
    Task<RconConnectionSettings?> GetAsync(CancellationToken cancellationToken = default);
}

public sealed record RconConnectionSettings(string Host, int Port, string Password);
