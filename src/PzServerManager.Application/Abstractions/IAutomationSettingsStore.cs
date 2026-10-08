using PzServerManager.Application.Automation;

namespace PzServerManager.Application.Abstractions;

public interface IAutomationSettingsStore
{
    Task<AutomationSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AutomationSettings settings, CancellationToken cancellationToken = default);
}
