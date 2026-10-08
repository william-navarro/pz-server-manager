using System.Text.Json;
using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Cardinal;
using PzServerManager.Application.Rcon;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.Cardinal;

public sealed class CardinalActionExecutor(
    IOptions<ManagerOptions> options,
    IRconClient rcon,
    IAuditLog audit) : ICardinalActionExecutor
{
    private readonly string _actionsPath = Path.Combine(options.Value.ManagedServer!.DataPath, "Lua", "cardinal", "acoes");

    public async Task<CardinalActionResult> ExecuteAsync(CardinalActionRequest request, CardinalSettings settings, CancellationToken cancellationToken = default)
    {
        CardinalRequestValidator.Validate(request, settings);
        try
        {
            var command = BuildRconCommand(request);
            if (command is not null)
            {
                var response = await rcon.ExecuteAsync(command, cancellationToken);
                await audit.WriteAsync(request.RequestedBy, $"Cardinal: {CardinalActionCatalog.Label(request.Kind)}", "sucesso", CancellationToken.None);
                return new(CardinalEventStatus.Executado, string.IsNullOrWhiteSpace(response) ? "Comando aceito pelo servidor." : response.Trim());
            }

            Directory.CreateDirectory(_actionsPath);
            var action = new Dictionary<string, object?>
            {
                ["id"] = request.Id,
                ["type"] = BridgeType(request.Kind),
                ["player"] = request.Player,
                ["amount"] = request.Amount,
                ["distance"] = request.Distance,
                ["itemType"] = request.ItemType,
                ["message"] = request.Message,
                ["force"] = request.OverrideProtections,
                ["parameters"] = request.Parameters
            };
            var destination = Path.Combine(_actionsPath, $"{request.Id}.txt");
            var temporary = Path.Combine(_actionsPath, $".{request.Id}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, action, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
            File.Move(temporary, destination, false);
            await audit.WriteAsync(request.RequestedBy, $"Cardinal: {CardinalActionCatalog.Label(request.Kind)}", "enfileirado", CancellationToken.None);
            return new(CardinalEventStatus.Enfileirado, "Ação enfileirada para a ponte Lua.");
        }
        catch (Exception exception)
        {
            await audit.WriteAsync(request.RequestedBy, $"Cardinal: {CardinalActionCatalog.Label(request.Kind)}", $"erro: {exception.Message}", CancellationToken.None);
            return new(CardinalEventStatus.Falhou, exception.Message);
        }
    }

    private static string? BuildRconCommand(CardinalActionRequest request) => request.Kind switch
    {
        CardinalActionKind.Helicoptero => "chopper",
        CardinalActionKind.TiroDistante => "gunshot",
        CardinalActionKind.Chuva => "startrain",
        CardinalActionKind.Trovao => "thunder",
        CardinalActionKind.Raio => "lightning",
        CardinalActionKind.Mensagem => RconCommands.ServerMessage($"[Cardinal] {request.Message ?? "O mundo está mudando."}"),
        _ => null
    };

    private static string BridgeType(CardinalActionKind kind) => kind switch
    {
        CardinalActionKind.HordaDirecionada => "horda_direcionada",
        CardinalActionKind.HordaTematica => "horda_tematica",
        CardinalActionKind.Som => "som",
        CardinalActionKind.Alarme => "alarme",
        CardinalActionKind.Tempestade => "tempestade",
        CardinalActionKind.Neblina => "neblina",
        CardinalActionKind.RaidAlife => "alife_raid",
        CardinalActionKind.ScoutsAlife => "alife_scouts",
        CardinalActionKind.ItemRaro => "item_raro",
        CardinalActionKind.Esconderijo => "esconderijo",
        CardinalActionKind.Airdrop => "airdrop",
        CardinalActionKind.VeiculoAbandonado => "veiculo_abandonado",
        _ => throw new InvalidOperationException("A ação não está na lista branca da ponte Cardinal.")
    };
}
