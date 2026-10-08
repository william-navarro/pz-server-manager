using PzServerManager.Infrastructure.ManagedServer;

namespace PzServerManager.Tests;

public sealed class SystemdShowParserTests
{
    [Fact]
    public void Parse_ReadsSystemctlProperties()
    {
        const string text = "ActiveState=active\nSubState=running\nMainPID=4321\nActiveEnterTimestamp=Fri 2026-10-02 21:09:45 -03\nNRestarts=4\nMemoryCurrent=2147483648\nCPUUsageNSec=9876543210\n";

        var result = SystemdShowParser.Parse(text);

        Assert.Equal("active", result.ActiveState);
        Assert.Equal("running", result.SubState);
        Assert.Equal(4321, result.MainProcessId);
        Assert.Equal(4, result.RestartCount);
        Assert.Equal(2147483648, result.MemoryCurrent);
        Assert.Equal(9876543210UL, result.CpuUsageNanoseconds);
        Assert.Equal(TimeSpan.FromHours(-3), result.ActiveEnterTimestamp?.Offset);
    }

    [Fact]
    public void Parse_TreatsZeroPidAndInvalidValuesAsUnavailable()
    {
        var result = SystemdShowParser.Parse("MainPID=0\nNRestarts=inválido\nActiveEnterTimestamp=\n");

        Assert.Null(result.MainProcessId);
        Assert.Null(result.ActiveEnterTimestamp);
        Assert.Equal(0, result.RestartCount);
        Assert.Null(result.MemoryCurrent);
        Assert.Null(result.CpuUsageNanoseconds);
    }
}
