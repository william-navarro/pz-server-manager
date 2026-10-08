using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;

namespace PzServerManager.Infrastructure.Configuration;

internal sealed class ManagerPaths : IManagerPaths
{
    public ManagerPaths(IOptions<ManagerOptions> options)
    {
        var expanded = Environment.ExpandEnvironmentVariables(options.Value.DataRoot);
        DataRoot = Path.GetFullPath(expanded);
        DatabasePath = Path.Combine(DataRoot, "data", "manager.db");
        InstancesRoot = Path.Combine(DataRoot, "instances");
        SteamCmdRoot = Path.Combine(DataRoot, "steamcmd");
        BackupsRoot = Path.Combine(DataRoot, "backups");
        LogsRoot = Path.Combine(DataRoot, "logs");
    }

    public string DataRoot { get; }
    public string DatabasePath { get; }
    public string InstancesRoot { get; }
    public string SteamCmdRoot { get; }
    public string BackupsRoot { get; }
    public string LogsRoot { get; }
}
