namespace PzServerManager.Application.Abstractions;

public interface IServerOperationLock
{
    bool IsBusy { get; }
    string? CurrentOperation { get; }
    IDisposable? TryAcquire(string operation);
}
