using System.Collections.Concurrent;

namespace PzServerManager.Infrastructure.Authentication;

public sealed class LoginAttemptLimiter(TimeProvider timeProvider)
{
    public const int MaximumFailures = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, AttemptState> _attempts = new(StringComparer.OrdinalIgnoreCase);

    public LoginAttemptLimiter() : this(TimeProvider.System)
    {
    }

    public bool IsBlocked(string userName, string ipAddress)
    {
        var now = timeProvider.GetUtcNow();
        return IsKeyBlocked(UserKey(userName), now) || IsKeyBlocked(IpKey(ipAddress), now);
    }

    public void RecordFailure(string userName, string ipAddress)
    {
        var now = timeProvider.GetUtcNow();
        RecordFailure(UserKey(userName), now);
        RecordFailure(IpKey(ipAddress), now);
    }

    public void RecordSuccess(string userName, string ipAddress)
    {
        _attempts.TryRemove(UserKey(userName), out _);
        _attempts.TryRemove(IpKey(ipAddress), out _);
    }

    private bool IsKeyBlocked(string key, DateTimeOffset now)
    {
        if (!_attempts.TryGetValue(key, out var state))
        {
            return false;
        }

        lock (state)
        {
            if (state.BlockedUntil is not null && state.BlockedUntil > now)
            {
                return true;
            }

            if (state.BlockedUntil is not null)
            {
                _attempts.TryRemove(key, out _);
            }

            return false;
        }
    }

    private void RecordFailure(string key, DateTimeOffset now)
    {
        var state = _attempts.GetOrAdd(key, _ => new AttemptState());
        lock (state)
        {
            if (state.BlockedUntil is not null && state.BlockedUntil <= now)
            {
                state.FailureCount = 0;
                state.BlockedUntil = null;
            }

            state.FailureCount++;
            if (state.FailureCount >= MaximumFailures)
            {
                state.BlockedUntil = now.Add(LockoutDuration);
            }
        }
    }

    private static string UserKey(string userName) => $"user:{userName.Trim()}";
    private static string IpKey(string ipAddress) => $"ip:{ipAddress.Trim()}";

    private sealed class AttemptState
    {
        public int FailureCount { get; set; }
        public DateTimeOffset? BlockedUntil { get; set; }
    }
}
