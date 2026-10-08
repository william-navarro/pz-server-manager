namespace PzServerManager.Application.ManagedServer;

public enum ManagedServerState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
    Unknown
}

public sealed record ManagedServerStatus(
    ManagedServerState State,
    string ActiveState,
    string SubState,
    int? MainProcessId,
    DateTimeOffset? ActiveSince,
    int RestartCount,
    long? ProcessMemoryBytes,
    long? AvailableMemoryBytes,
    long? TotalMemoryBytes,
    ulong? CpuUsageNanoseconds = null);
