using PzServerManager.Application.Rcon;

namespace PzServerManager.Tests;

public sealed class RconCommandsTests
{
    [Fact]
    public void AccessLevels_MatchBuild4221()
    {
        Assert.Equal(["User", "Priority", "Admin", "Moderator", "GM", "Observer"], Enum.GetNames<ServerAccessLevel>());
        Assert.DoesNotContain("Banned", Enum.GetNames<ServerAccessLevel>());
    }

    [Fact]
    public void Commands_AreBuiltExactly()
    {
        Assert.Equal("kickuser \"Ana Maria\" -r \"spam\"", RconCommands.Kick("Ana Maria", "spam"));
        Assert.Equal("banuser \"Ana\" -ip -r \"motivo\"", RconCommands.Ban("Ana", "motivo", true));
        Assert.Equal("banuser \"Ana\" -r \"motivo\"", RconCommands.Ban("Ana", "motivo", false));
        Assert.Equal("unbanuser \"Ana\"", RconCommands.Unban("Ana"));
        Assert.Equal("setaccesslevel \"Ana\" \"gm\"", RconCommands.SetAccessLevel("Ana", ServerAccessLevel.GM));
        Assert.Equal("setaccesslevel \"Ana\" \"user\"", RconCommands.SetAccessLevel("Ana", ServerAccessLevel.User));
        Assert.Equal("setaccesslevel \"Ana\" \"priority\"", RconCommands.SetAccessLevel("Ana", ServerAccessLevel.Priority));
        Assert.Equal("servermsg \"Olá a todos\"", RconCommands.ServerMessage("Olá a todos"));
        Assert.Equal("adduser \"Ana\" \"S3nhA9876543\"", RconCommands.AddUser("Ana", "S3nhA9876543"));
        Assert.Equal("removeuserfromwhitelist \"Ana\"", RconCommands.RemoveUser("Ana"));
        Assert.Equal("setpassword \"Ana\" \"Nova98765432\"", RconCommands.SetPassword("Ana", "Nova98765432"));
    }

    [Theory]
    [InlineData("nome\"ruim")]
    [InlineData("nome\nruim")]
    [InlineData("nome/ruim")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234567")]
    public void UserName_RejectsUnsafeValues(string value) =>
        Assert.Throws<ArgumentException>(() => RconArgumentValidator.UserName(value));

    [Theory]
    [InlineData("texto \"injetado\"")]
    [InlineData("linha 1\nlinha 2")]
    [InlineData("controle\u0001")]
    public void Text_RejectsUnsafeValues(string value) =>
        Assert.Throws<ArgumentException>(() => RconArgumentValidator.Text(value));

    [Fact]
    public void Text_RejectsMoreThanTwoHundredCharacters() =>
        Assert.Throws<ArgumentException>(() => RconCommands.ServerMessage(new string('x', 201)));
}
