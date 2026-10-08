using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Cardinal;
using PzServerManager.Application.Rcon;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.Cardinal;

public sealed class CardinalBridgeResultCollector(
    IOptions<ManagerOptions> options,
    ICardinalStore store,
    IRconClient rcon,
    IAuditLog audit,
    ILogger<CardinalBridgeResultCollector> logger)
{
    private sealed record BridgeResult(string Id, bool Success, string? Message);
    private readonly string _resultsPath = Path.Combine(options.Value.ManagedServer!.DataPath, "Lua", "cardinal", "acoes");
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public async Task CollectAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_resultsPath)) return;
        foreach (var path in Directory.EnumerateFiles(_resultsPath, "resultado-*.txt").OrderBy(path => path, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (!_seen.Add(name)) continue;
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var result = await JsonSerializer.DeserializeAsync<BridgeResult>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
                if (result is null) continue;
                var dashboard = await store.ReadAsync(cancellationToken);
                var original = dashboard.Timeline.FirstOrDefault(item => item.Id == result.Id);
                var alreadyRecorded = dashboard.Timeline.Any(item => item.Id == $"{result.Id}-resultado");
                if (original is not null && !alreadyRecorded) await RecordAsync(original, result, cancellationToken);
                // Limpa a fila: primeiro a ação (senão a ponte, sem o resultado, executaria de novo), depois o resultado.
                RemoveProcessed(path, result.Id);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                _seen.Remove(name);
                logger.LogDebug(exception, "Resultado {ResultFile} ainda não está pronto", name);
            }
        }
    }

    private void RemoveProcessed(string resultPath, string id)
    {
        try
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,80}$")) return;
            File.Delete(Path.Combine(_resultsPath, $"{id}.txt"));
            File.Delete(resultPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Não consegui limpar a ação {ActionId} da fila da ponte", id);
        }
    }

    private async Task RecordAsync(CardinalTimelineEvent original, BridgeResult result, CancellationToken cancellationToken)
    {
        await store.AppendTimelineAsync(original with
        {
            Id = $"{result.Id}-resultado",
            Timestamp = DateTimeOffset.UtcNow,
            Source = CardinalDecisionSource.Ponte,
            Reason = "retorno da ponte Lua",
            Status = result.Success ? CardinalEventStatus.Executado : CardinalEventStatus.Falhou,
            Result = result.Message ?? (result.Success ? "Ação executada." : "A ponte recusou a ação.")
        }, cancellationToken);
        if (result.Success && original.Action is CardinalActionKind.Esconderijo or CardinalActionKind.Airdrop or CardinalActionKind.VeiculoAbandonado &&
            !string.IsNullOrWhiteSpace(result.Message))
        {
            var clue = RconArgumentValidator.Text($"[Cardinal] {result.Message}", "pista narrativa", 200);
            await rcon.ExecuteAsync(RconCommands.ServerMessage(clue), cancellationToken);
            await audit.WriteAsync("Cardinal", "enviar pista de suprimento", "sucesso", CancellationToken.None);
        }
    }
}
