using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Tests;

public sealed class ManagerOptionsValidatorTests
{
    [Fact]
    public void Validate_AcceptsMissingManagedServer()
    {
        var result = new ManagerOptionsValidator().Validate(null, new ManagerOptions { DataRoot = "data" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_AcceptsValidLinuxConfiguration()
    {
        var result = new ManagerOptionsValidator().Validate(null, CreateValidOptions());

        Assert.True(result.Succeeded);
        foreach (var invalidName in new[] { "server.name", "server name", new string('a', 33) })
        {
            var invalid = CreateValidOptions();
            invalid.ManagedServer!.ServerName = invalidName;
            Assert.True(new ManagerOptionsValidator().Validate(null, invalid).Failed);
        }
    }

    [Theory]
    [InlineData("pzserver.service;reboot")]
    [InlineData("pz server.service")]
    [InlineData("pzserver/service")]
    public void Validate_RejectsInvalidServiceName(string serviceName)
    {
        var options = CreateValidOptions();
        options.ManagedServer!.ServiceName = serviceName;

        Assert.True(new ManagerOptionsValidator().Validate(null, options).Failed);
    }

    [Theory]
    [InlineData("InstallPath")]
    [InlineData("DataPath")]
    [InlineData("ConsoleFifo")]
    [InlineData("BackupsPath")]
    public void Validate_RejectsRelativeManagedPaths(string property)
    {
        var options = CreateValidOptions();
        typeof(ManagedServerOptions).GetProperty(property)!.SetValue(options.ManagedServer, "relative/path");

        Assert.True(new ManagerOptionsValidator().Validate(null, options).Failed);
    }

    private static ManagerOptions CreateValidOptions() => new()
    {
        DataRoot = "/var/lib/pzmanager",
        ManagedServer = new ManagedServerOptions
        {
            DisplayName = "Test server",
            ServiceName = "pzserver.service",
            InstallPath = "/opt/pzserver/pz",
            DataPath = "/opt/pzserver/Zomboid",
            ServerName = "servertest",
            ConsoleFifo = "/run/pzserver/console"
        }
    };
}
