using PzServerManager.Application.Rcon;

namespace PzServerManager.Tests;

public sealed class RconParsingTests
{
    [Theory]
    [InlineData("Players connected (0): \n", new string[0])]
    [InlineData("Players connected (1): \n-Ana\n", new[] { "Ana" })]
    [InlineData("Players connected (3): \r\n-Ana\r\n-Bob Smith\r\n-C-3\r\n", new[] { "Ana", "Bob Smith", "C-3" })]
    public void ParsePlayers_ReadsRealFormat(string output, string[] expected) =>
        Assert.Equal(expected, RconOutputParser.ParsePlayers(output));

    [Fact]
    public void ParsePlayers_UsesRealFixture()
    {
        var output = FixtureSection("players");
        Assert.Empty(RconOutputParser.ParsePlayers(output));
    }

    [Fact]
    public void ParseHelp_ListsCommandsFromRealFixture()
    {
        var commands = RconOutputParser.ParseHelp(FixtureSection("help"));
        Assert.Contains("players", commands);
        Assert.Contains("adduser", commands);
        Assert.Contains("setpassword", commands);
        Assert.Contains("worldgen", commands);
        Assert.True(commands.Count > 50);
    }

    internal static string FixtureSection(string command)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rcon-saida.txt"));
        var marker = $"@@@ {command} ";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        start = text.IndexOf('\n', start) + 1;
        var end = text.IndexOf("@@@ ", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
