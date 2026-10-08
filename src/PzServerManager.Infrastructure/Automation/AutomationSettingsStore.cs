using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Automation;

namespace PzServerManager.Infrastructure.Automation;

public sealed class AutomationSettingsStore(IManagerPaths paths) : IAutomationSettingsStore
{
    private readonly string _path = Path.Combine(paths.DataRoot, "automation-settings.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AutomationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return (await Configuration.AtomicJsonFile.ReadAsync<AutomationSettings>(_path, cancellationToken) ?? new()).Validate(); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(AutomationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Validate();
        await _gate.WaitAsync(cancellationToken);
        try { await Configuration.AtomicJsonFile.WriteAsync(_path, settings, cancellationToken); }
        finally { _gate.Release(); }
    }
}
