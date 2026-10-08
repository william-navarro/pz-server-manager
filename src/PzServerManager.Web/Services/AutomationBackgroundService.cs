using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Automation;
using PzServerManager.Application.ManagedServer;
using PzServerManager.Application.Metrics;
using PzServerManager.Application.Rcon;
using PzServerManager.Application.Updates;

namespace PzServerManager.Web.Services;

public sealed class AutomationBackgroundService(
    IAutomationSettingsStore settingsStore,
    IUpdateManager updates,
    IServerController controller,
    ServerAdministrationService administration,
    ServerRestartCoordinator restarts,
    IMetricsStore metrics,
    TimeProvider timeProvider,
    ILogger<AutomationBackgroundService> logger) : BackgroundService
{
    private DateTimeOffset? _lastUpdateCheck;
    private DateTimeOffset? _lastMetric;
    private DateOnly? _lastDailyRestart;
    private ulong? _previousCpu;
    private DateTimeOffset? _previousCpuAt;
    private DateTimeOffset? _previousActiveSince;
    private string? _lastAutoSignature;
    private bool _loopWarned;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), timeProvider);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha no ciclo de automação do servidor");
                try { await Task.Delay(TimeSpan.FromSeconds(15), timeProvider, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken token)
    {
        var now = timeProvider.GetUtcNow();
        var settings = await settingsStore.LoadAsync(token);
        if (_lastMetric is null || now - _lastMetric >= TimeSpan.FromMinutes(1))
        {
            await CollectMetricAsync(now, token); _lastMetric = now;
        }
        if (_lastUpdateCheck is null || now - _lastUpdateCheck >= TimeSpan.FromMinutes(settings.CheckIntervalMinutes))
        {
            _lastUpdateCheck = now;
            var overview = await updates.CheckNowAsync("automação", token);
            if (settings.UpdateMode == UpdateMode.Automatico && overview.ModsCheckSucceeded && !updates.IsBusy &&
                (overview.OutdatedMods.Count > 0 || overview.IsGameOutdated))
            {
                // Se o mesmo conjunto continua pendente depois de uma tentativa (o servidor não baixou o mod, por exemplo),
                // não reinicia de novo em loop: fica para o admin resolver pelo botão "Atualizar agora".
                var signature = overview.PendingSignature;
                if (signature == _lastAutoSignature)
                {
                    if (!_loopWarned) logger.LogWarning("Atualização automática já tentada e ainda pendente ({Signature}); aguardando o admin", signature);
                    _loopWarned = true;
                }
                else
                {
                    _lastAutoSignature = signature;
                    _loopWarned = false;
                    _ = ApplyAutomaticUpdateAsync(token);
                }
            }
        }
        await CheckDailyRestartAsync(settings, token);
    }

    private async Task CollectMetricAsync(DateTimeOffset now, CancellationToken token)
    {
        var status = await controller.GetStatusAsync(token);
        double? cpu = null;
        var restarted = _previousActiveSince is not null && status.ActiveSince is not null && status.ActiveSince != _previousActiveSince;
        if (!restarted && status.CpuUsageNanoseconds is not null && _previousCpu is not null && _previousCpuAt is not null && status.CpuUsageNanoseconds >= _previousCpu)
        {
            var elapsed = (now - _previousCpuAt.Value).TotalSeconds;
            if (elapsed > 0) cpu = (status.CpuUsageNanoseconds.Value - _previousCpu.Value) / 1_000_000_000d / elapsed * 100d;
        }
        _previousCpu = status.CpuUsageNanoseconds; _previousCpuAt = now;
        int? players = null;
        if (status.State == ManagedServerState.Running)
        {
            try { players = (await administration.GetPlayersAsync(token)).Count; } catch { }
        }
        else players = 0;
        await metrics.AppendSampleAsync(new(now, status.ProcessMemoryBytes, cpu, players), token);
        if (restarted && status.ActiveSince is { } activeSince)
            await metrics.AppendEventAsync(new(activeSince, "reinicio", "Servidor reiniciado"), token);
        _previousActiveSince = status.ActiveSince;
    }

    private Task CheckDailyRestartAsync(AutomationSettings settings, CancellationToken token)
    {
        if (!settings.DailyRestartEnabled || !TimeOnly.TryParseExact(settings.DailyRestartTime, "HH:mm", out var configured)) return Task.CompletedTask;
        var local = timeProvider.GetLocalNow();
        var today = DateOnly.FromDateTime(local.DateTime);
        var difference = local.TimeOfDay - configured.ToTimeSpan();
        if (difference < TimeSpan.Zero || difference >= TimeSpan.FromMinutes(1) || _lastDailyRestart == today) return Task.CompletedTask;
        _lastDailyRestart = today;
        _ = RunDailyRestartAsync(token);
        return Task.CompletedTask;
    }

    private async Task ApplyAutomaticUpdateAsync(CancellationToken token)
    {
        try { await updates.ApplyAsync("automação", UpdateTarget.All, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Falha ao aplicar atualização automática"); }
    }

    private async Task RunDailyRestartAsync(CancellationToken token)
    {
        try { await restarts.RestartImmediatelyIfEmptyAsync("automação", "Reinício diário programado", token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Falha no reinício diário"); }
    }
}
