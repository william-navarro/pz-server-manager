namespace PzServerManager.Infrastructure.Configuration;

public sealed class ManagedServerOptions
{
    public string DisplayName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public string DataPath { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string ConsoleFifo { get; set; } = string.Empty;
    public string BackupsPath { get; set; } = "/opt/pzserver/backups";
    public string ServerVersion { get; set; } = "42.21";
    public string CardinalKeyPath { get; set; } = "/etc/pzmanager/cardinal.key";

    public string ServerIniPath => Path.Combine(DataPath, "Server", $"{ServerName}.ini");
    public string SandboxVarsPath => Path.Combine(DataPath, "Server", $"{ServerName}_SandboxVars.lua");
}
