namespace PzServerManager.Infrastructure;

public interface IInfrastructureInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
