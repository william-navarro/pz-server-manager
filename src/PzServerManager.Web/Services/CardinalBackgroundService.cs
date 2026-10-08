using PzServerManager.Application.Abstractions;
using PzServerManager.Infrastructure.Cardinal;

namespace PzServerManager.Web.Services;

public sealed class CardinalBackgroundService(
    CardinalLogCollector collector,
    CardinalBridgeResultCollector results,
    ICardinalManager manager,
    TimeProvider timeProvider,
    ILogger<CardinalBackgroundService> logger) : BackgroundService
{
    private DateTimeOffset? _lastMinute;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), timeProvider);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await results.CollectAsync(stoppingToken);
                var now = timeProvider.GetUtcNow();
                if (_lastMinute is null || now - _lastMinute >= TimeSpan.FromMinutes(1))
                {
                    _lastMinute = now;
                    await collector.CollectAsync(stoppingToken);
                    await manager.RunDirectorCycleAsync(stoppingToken);
                }
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha no ciclo da Cardinal");
                try { await Task.Delay(TimeSpan.FromSeconds(10), timeProvider, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
