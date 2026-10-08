namespace PzServerManager.Application.Abstractions;

public interface IAuditLog
{
    Task WriteAsync(string userName, string action, string result, CancellationToken cancellationToken = default);
}
