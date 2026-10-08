namespace PzServerManager.Application.Abstractions;

public interface IManagerPaths
{
    string DataRoot { get; }
    string DatabasePath { get; }
    string InstancesRoot { get; }
    string SteamCmdRoot { get; }
    string BackupsRoot { get; }
    string LogsRoot { get; }
}
