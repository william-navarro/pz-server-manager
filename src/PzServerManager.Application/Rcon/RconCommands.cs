namespace PzServerManager.Application.Rcon;

public enum ServerAccessLevel
{
    User,
    Priority,
    Admin,
    Moderator,
    GM,
    Observer
}

public static class RconCommands
{
    public const string Players = "players";
    public const string Help = "help";
    public const string Save = "save";

    public static string Kick(string userName, string reason) =>
        $"kickuser \"{RconArgumentValidator.UserName(userName)}\" -r \"{RconArgumentValidator.Text(reason, "motivo")}\"";

    public static string Ban(string userName, string reason, bool alsoBanIp) =>
        $"banuser \"{RconArgumentValidator.UserName(userName)}\"{(alsoBanIp ? " -ip" : string.Empty)} -r \"{RconArgumentValidator.Text(reason, "motivo")}\"";

    public static string Unban(string userName) =>
        $"unbanuser \"{RconArgumentValidator.UserName(userName)}\"";

    public static string SetAccessLevel(string userName, ServerAccessLevel level) =>
        $"setaccesslevel \"{RconArgumentValidator.UserName(userName)}\" \"{level.ToString().ToLowerInvariant()}\"";

    public static string ServerMessage(string message) =>
        $"servermsg \"{RconArgumentValidator.Text(message, "mensagem")}\"";

    public static string AddUser(string userName, string password) =>
        $"adduser \"{RconArgumentValidator.UserName(userName)}\" \"{RconArgumentValidator.Password(password)}\"";

    public static string RemoveUser(string userName) =>
        $"removeuserfromwhitelist \"{RconArgumentValidator.UserName(userName)}\"";

    public static string SetPassword(string userName, string password) =>
        $"setpassword \"{RconArgumentValidator.UserName(userName)}\" \"{RconArgumentValidator.Password(password)}\"";
}
