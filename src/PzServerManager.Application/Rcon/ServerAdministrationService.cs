using PzServerManager.Application.Abstractions;

namespace PzServerManager.Application.Rcon;

public sealed class ServerAdministrationService(IRconClient rcon, IAuditLog auditLog)
{
    public async Task<IReadOnlyList<string>> GetPlayersAsync(CancellationToken cancellationToken = default) =>
        RconOutputParser.ParsePlayers(await rcon.ExecuteAsync(RconCommands.Players, cancellationToken));

    public Task<string> KickAsync(string actor, string userName, string reason, CancellationToken ct = default) =>
        ExecuteAuditedAsync(actor, $"expulsar jogador {RconArgumentValidator.UserName(userName)}", RconCommands.Kick(userName, reason), ct);

    public Task<string> BanAsync(string actor, string userName, string reason, bool alsoBanIp, CancellationToken ct = default) =>
        ExecuteAuditedAsync(actor, $"banir jogador {RconArgumentValidator.UserName(userName)}{(alsoBanIp ? " e IP" : string.Empty)}", RconCommands.Ban(userName, reason, alsoBanIp), ct);

    public Task<string> UnbanAsync(string actor, string userName, CancellationToken ct = default) =>
        ExecuteAuditedAsync(actor, $"desbanir jogador {RconArgumentValidator.UserName(userName)}", RconCommands.Unban(userName), ct);

    public Task<string> SetAccessLevelAsync(string actor, string userName, ServerAccessLevel level, CancellationToken ct = default) =>
        ExecuteAuditedAsync(actor, $"alterar acesso de {RconArgumentValidator.UserName(userName)} para {level}", RconCommands.SetAccessLevel(userName, level), ct);

    public Task<string> SendMessageAsync(string actor, string message, CancellationToken ct = default) =>
        ExecuteAuditedAsync(actor, "enviar mensagem global", RconCommands.ServerMessage(message), ct);

    public Task<string> SaveAsync(string actor, CancellationToken ct = default) =>
        ExecuteAuditedAsync(actor, "salvar mundo", RconCommands.Save, ct);

    public Task<string> AddUserAsync(string actor, string userName, string password, CancellationToken ct = default) =>
        ExecuteAuditedAsync(actor, $"criar conta {RconArgumentValidator.UserName(userName)}", RconCommands.AddUser(userName, password), ct);

    public Task<string> RemoveUserAsync(string actor, string userName, CancellationToken ct = default) =>
        ExecuteAuditedAsync(actor, $"remover conta {RconArgumentValidator.UserName(userName)}", RconCommands.RemoveUser(userName), ct);

    public Task<string> SetPasswordAsync(string actor, string userName, string password, CancellationToken ct = default) =>
        ExecuteAuditedAsync(actor, $"trocar senha de {RconArgumentValidator.UserName(userName)}", RconCommands.SetPassword(userName, password), ct);

    private async Task<string> ExecuteAuditedAsync(string actor, string action, string command, CancellationToken ct)
    {
        try
        {
            var response = await rcon.ExecuteAsync(command, ct);
            await auditLog.WriteAsync(actor, action, "sucesso", CancellationToken.None);
            return response;
        }
        catch (Exception exception)
        {
            await auditLog.WriteAsync(actor, action, $"erro: {exception.Message}", CancellationToken.None);
            throw;
        }
    }
}
