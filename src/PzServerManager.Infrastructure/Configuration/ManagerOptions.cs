namespace PzServerManager.Infrastructure.Configuration;

public sealed class ManagerOptions
{
    public const string SectionName = "PzManager";

    public string DataRoot { get; set; } = "%LOCALAPPDATA%\\PzServerManager";
    public string? ListenUrl { get; set; }
    public bool RequireHttpsCookie { get; set; }
    public ManagedServerOptions? ManagedServer { get; set; }
}
