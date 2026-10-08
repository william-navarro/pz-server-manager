using PzServerManager.Infrastructure.ManagedServer;

namespace PzServerManager.Tests;

public sealed class ProcFileParserTests
{
    [Fact]
    public void ParseProcessResidentMemory_ReadsVmRssInBytes()
    {
        var result = ProcFileParser.ParseProcessResidentMemory("Name:\tjava\nVmSize:\t900000 kB\nVmRSS:\t123456 kB\n");

        Assert.Equal(123456L * 1024, result);
    }

    [Fact]
    public void ParseSystemMemory_ReadsAvailableAndTotalInBytes()
    {
        var result = ProcFileParser.ParseSystemMemory("MemTotal:       16000000 kB\nMemFree: 10 kB\nMemAvailable:    8000000 kB\n");

        Assert.Equal(8000000L * 1024, result.AvailableBytes);
        Assert.Equal(16000000L * 1024, result.TotalBytes);
    }
}
