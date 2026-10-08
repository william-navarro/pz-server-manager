using PzServerManager.Infrastructure.Authentication;

namespace PzServerManager.Tests;

public sealed class LoginAttemptLimiterTests
{
    [Fact]
    public void FiveFailures_BlockSameUserAndIpForFifteenMinutes()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var limiter = new LoginAttemptLimiter(clock);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            limiter.RecordFailure("admin", "203.0.113.10");
        }

        Assert.True(limiter.IsBlocked("admin", "outro-ip"));
        Assert.True(limiter.IsBlocked("outro-usuario", "203.0.113.10"));
        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True(limiter.IsBlocked("admin", "203.0.113.10"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(limiter.IsBlocked("admin", "203.0.113.10"));
    }

    [Fact]
    public void Success_ClearsConsecutiveFailures()
    {
        var limiter = new LoginAttemptLimiter();
        for (var attempt = 0; attempt < 4; attempt++) limiter.RecordFailure("admin", "ip");

        limiter.RecordSuccess("admin", "ip");
        limiter.RecordFailure("admin", "ip");

        Assert.False(limiter.IsBlocked("admin", "ip"));
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
