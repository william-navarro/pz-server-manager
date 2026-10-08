using PzServerManager.Application.ManagedServer;

namespace PzServerManager.Tests;

public sealed class ServerConsoleCommandTests
{
    [Fact]
    public void Validate_TrimsValidCommand() =>
        Assert.Equal("save", ServerConsoleCommand.Validate("  save  "));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("save\nquit")]
    [InlineData("save\rquit")]
    [InlineData("save\n")]
    [InlineData("\rsave")]
    public void Validate_RejectsEmptyAndMultilineCommands(string command) =>
        Assert.Throws<ArgumentException>(() => ServerConsoleCommand.Validate(command));

    [Fact]
    public void Validate_RejectsCommandOverMaximumLength() =>
        Assert.Throws<ArgumentException>(() => ServerConsoleCommand.Validate(new string('a', 301)));
}
