using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Cardinal;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Infrastructure.Cardinal;

public sealed class AnthropicCardinalClient(
    IOptions<ManagerOptions> options,
    ILogger<AnthropicCardinalClient> logger) : ICardinalLlmClient
{
    private sealed record LlmAction(string? Type, string? Player, int Amount, int Distance, string? ItemType, string? Message);
    private sealed record LlmEnvelope(LlmAction[]? Actions, string? Narrative);
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.anthropic.com/"), Timeout = TimeSpan.FromSeconds(30) };
    private readonly string _keyPath = options.Value.ManagedServer!.CardinalKeyPath;

    public async Task<CardinalLlmSuggestion?> SuggestAsync(CardinalDashboard dashboard, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_keyPath)) return null;
        EnsurePrivateKeyFile(_keyPath);
        var key = (await File.ReadAllTextAsync(_keyPath, cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(key)) return null;
        var summary = BuildSummary(dashboard);
        var payload = new
        {
            model = "claude-haiku-4-5-20251001",
            max_tokens = 800,
            system = "Você é a Cardinal, mestre de RPG justo de um servidor Project Zomboid. Responda apenas JSON válido. " +
                "Use no máximo 2 ações da lista fornecida. Nunca persiga jogador fraco, dormindo ou cercado; as proteções finais são externas.",
            messages = new[] { new { role = "user", content = summary } }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Anthropic respondeu {(int)response.StatusCode}: {SafeError(responseText)}");
        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        var inputTokens = root.GetProperty("usage").GetProperty("input_tokens").GetInt32();
        var outputTokens = root.GetProperty("usage").GetProperty("output_tokens").GetInt32();
        var text = root.GetProperty("content").EnumerateArray()
            .FirstOrDefault(item => item.TryGetProperty("type", out var type) && type.GetString() == "text")
            .GetProperty("text").GetString() ?? "{}";
        text = StripFence(text);
        LlmEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<LlmEnvelope>(text, JsonOptions); }
        catch (JsonException exception) { logger.LogWarning(exception, "A Cardinal LLM devolveu JSON inválido"); return new([], null, inputTokens, outputTokens); }
        var actions = (envelope?.Actions ?? []).Take(2).Select(ToRequest).Where(item => item is not null).Cast<CardinalActionRequest>().ToArray();
        var narrative = envelope?.Narrative?.Trim();
        if (narrative?.Length > 160) narrative = narrative[..160];
        return new(actions, string.IsNullOrWhiteSpace(narrative) ? null : narrative, inputTokens, outputTokens);
    }

    private static string BuildSummary(CardinalDashboard dashboard)
    {
        var snapshot = dashboard.LatestSnapshot!;
        var compact = new
        {
            instrucoes = new
            {
                formato = new { actions = new[] { new { type = "horda_direcionada", player = "nome", amount = 5, distance = 30, itemType = (string?)null, message = (string?)null } }, narrative = "mensagem curta opcional" },
                tipos = new[] { "horda_direcionada", "horda_tematica", "som", "helicoptero", "alarme", "tiro_distante", "tempestade", "neblina", "alife_raid", "alife_scouts", "item_raro", "esconderijo", "airdrop", "veiculo_abandonado", "chuva", "trovao", "raio", "mensagem" },
                tom = "mestre de RPG; desafie com chance de fuga; recompensa para quem vai mal"
            },
            mundo = new { snapshot.Day, snapshot.Hour, snapshot.Minute, snapshot.Weather, snapshot.Night, snapshot.RainIntensity, snapshot.FogIntensity },
            jogadores = snapshot.Players.Select(player => new
            {
                player.Username, player.HealthPercent, player.HoursSurvived, player.Kills, player.Zombies30,
                player.Asleep, player.Sitting, player.Reading, player.InVehicle, player.VehicleSpeed,
                player.HasMeleeWeapon, player.WeaponAmmo, player.InventoryAmmo, player.GroupSize,
                player.Bleeding, player.Fracture, player.Infected, player.Moodles
            }),
            eventos = dashboard.Timeline.Take(12).Select(item => new { item.Timestamp, action = item.Action.ToString(), item.Player, item.Status, item.Reason })
        };
        var json = JsonSerializer.Serialize(compact, JsonOptions);
        return json.Length <= 8000 ? json : json[..8000];
    }

    private static CardinalActionRequest? ToRequest(LlmAction action)
    {
        if (action.Type is null || !Kinds.TryGetValue(action.Type, out var kind)) return null;
        return new()
        {
            Kind = kind, Player = action.Player, Amount = action.Amount,
            Distance = action.Distance == 0 ? 30 : action.Distance,
            ItemType = action.ItemType, Message = action.Message,
            RequestedBy = "Cardinal LLM", OverrideProtections = false
        };
    }

    private static void EnsurePrivateKeyFile(string path)
    {
        if (!OperatingSystem.IsLinux()) return;
        var mode = File.GetUnixFileMode(path);
        const UnixFileMode forbidden = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((mode & forbidden) != 0) throw new InvalidOperationException("cardinal.key deve ter permissão 0600.");
    }

    private static string StripFence(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("```", StringComparison.Ordinal)) return value;
        var newline = value.IndexOf('\n');
        var end = value.LastIndexOf("```", StringComparison.Ordinal);
        return newline >= 0 && end > newline ? value[(newline + 1)..end].Trim() : value;
    }

    private static string SafeError(string value) => value.Length <= 300 ? value : value[..300];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, CardinalActionKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["horda_direcionada"] = CardinalActionKind.HordaDirecionada, ["horda_tematica"] = CardinalActionKind.HordaTematica,
        ["som"] = CardinalActionKind.Som, ["helicoptero"] = CardinalActionKind.Helicoptero, ["alarme"] = CardinalActionKind.Alarme,
        ["tiro_distante"] = CardinalActionKind.TiroDistante, ["tempestade"] = CardinalActionKind.Tempestade, ["neblina"] = CardinalActionKind.Neblina,
        ["alife_raid"] = CardinalActionKind.RaidAlife, ["alife_scouts"] = CardinalActionKind.ScoutsAlife, ["item_raro"] = CardinalActionKind.ItemRaro,
        ["esconderijo"] = CardinalActionKind.Esconderijo, ["airdrop"] = CardinalActionKind.Airdrop, ["veiculo_abandonado"] = CardinalActionKind.VeiculoAbandonado,
        ["chuva"] = CardinalActionKind.Chuva, ["trovao"] = CardinalActionKind.Trovao, ["raio"] = CardinalActionKind.Raio, ["mensagem"] = CardinalActionKind.Mensagem
    };
}
