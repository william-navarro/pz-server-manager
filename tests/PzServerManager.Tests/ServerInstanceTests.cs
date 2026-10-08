using PzServerManager.Domain.Servers;

namespace PzServerManager.Tests;

public sealed class ServerInstanceTests
{
    [Fact]
    public void Create_NormalizesNameAndPaths()
    {
        var createdAt = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        var instance = ServerInstance.Create(
            "  Servidor Principal  ",
            "servidor-principal",
            @".\server",
            @".\data",
            ServerBranch.Stable,
            16261,
            27015,
            createdAt);

        Assert.Equal("Servidor Principal", instance.Name);
        Assert.Equal("servidor-principal", instance.Slug);
        Assert.True(Path.IsPathFullyQualified(instance.InstallationPath));
        Assert.True(Path.IsPathFullyQualified(instance.DataPath));
        Assert.Equal(ServerInstanceStatus.NotInstalled, instance.Status);
        Assert.Equal(createdAt, instance.CreatedAtUtc);
    }

    [Fact]
    public void Create_RejectsEqualGameAndRconPorts()
    {
        var exception = Assert.Throws<ArgumentException>(() => ServerInstance.Create(
            "Servidor",
            "servidor",
            @".\server",
            @".\data",
            ServerBranch.Stable,
            16261,
            16261));

        Assert.Contains("devem ser diferentes", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Create_RejectsInvalidGamePort(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ServerInstance.Create(
            "Servidor",
            "servidor",
            @".\server",
            @".\data",
            ServerBranch.Stable,
            port,
            27015));
    }
}
