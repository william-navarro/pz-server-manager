namespace PzServerManager.Domain.Servers;

public enum ServerInstanceStatus
{
    NotInstalled = 0,
    Installing = 1,
    Stopped = 2,
    Running = 3,
    Updating = 4,
    Faulted = 5
}
