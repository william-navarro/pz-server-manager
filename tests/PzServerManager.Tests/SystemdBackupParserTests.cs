using PzServerManager.Infrastructure.ManagedServer;

namespace PzServerManager.Tests;

public sealed class SystemdBackupParserTests
{
    [Fact]
    public void ParseNextElapse_ParsesSystemdTimestamp()
    {
        var result = SystemdBackupParser.ParseNextElapse("NextElapseUSecRealtime=Sun 2026-10-04 05:00:00 -03\n");

        Assert.NotNull(result);
        Assert.Equal(new TimeSpan(-3, 0, 0), result.Value.Offset);
        Assert.Equal(5, result.Value.Hour);
    }

    [Theory]
    [InlineData("NextElapseUSecRealtime=\n")]
    [InlineData("NextElapseUSecRealtime=n/a\n")]
    [InlineData("NextElapseUSecRealtime=garbage\n")]
    public void ParseNextElapse_ReturnsNullForUnavailableValues(string output) =>
        Assert.Null(SystemdBackupParser.ParseNextElapse(output));

    [Fact]
    public void ParseServiceResults_MapsBlocksInUnitOrder()
    {
        const string output = "Result=success\nExecMainExitTimestamp=Sat 2026-10-03 12:00:01 -03\n\n" +
                              "Result=exit-code\nExecMainExitTimestamp=Sat 2026-10-03 05:00:04 -03\n";

        var results = SystemdBackupParser.ParseServiceResults(output, ["manual.service", "daily.service"]);

        Assert.Equal("manual.service", results[0].UnitName);
        Assert.Equal("success", results[0].Result);
        Assert.Equal(12, results[0].FinishedAt!.Value.Hour);
        Assert.Equal("daily.service", results[1].UnitName);
        Assert.Equal("exit-code", results[1].Result);
    }

    [Fact]
    public void ParseOperationState_ReadsActiveStateAndResult()
    {
        var result = SystemdBackupParser.ParseOperationState("Result=success\nActiveState=inactive\n");
        Assert.Equal("inactive", result.ActiveState);
        Assert.Equal("success", result.Result);
    }
}
