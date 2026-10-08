using PzServerManager.Application.Abstractions;

namespace PzServerManager.Application.ManagedServer;

public sealed class ServerOperationLock : IServerOperationLock
{
    private readonly object _sync = new();
    private string? _currentOperation;

    public bool IsBusy
    {
        get { lock (_sync) return _currentOperation is not null; }
    }

    public string? CurrentOperation
    {
        get { lock (_sync) return _currentOperation; }
    }

    public IDisposable? TryAcquire(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        lock (_sync)
        {
            if (_currentOperation is not null) return null;
            _currentOperation = operation;
            return new Lease(this);
        }
    }

    private void Release()
    {
        lock (_sync) _currentOperation = null;
    }

    private sealed class Lease(ServerOperationLock owner) : IDisposable
    {
        private ServerOperationLock? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
