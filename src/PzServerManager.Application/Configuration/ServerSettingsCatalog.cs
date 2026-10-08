using System.Globalization;
using System.Text.RegularExpressions;

namespace PzServerManager.Application.Configuration;

public static partial class ServerSettingsCatalog
{
    private static readonly Dictionary<string, string> ProtectedKeys = new(StringComparer.Ordinal)
    {
        ["Mods"] = "Gerencie esta chave na página de mods.",
        ["WorkshopItems"] = "Gerencie esta chave na página de mods.",
        ["Map"] = "Gerencie esta chave na página de mods.",
        ["RCONPort"] = "A porta RCON é uma configuração operacional do painel.",
        ["RCONPassword"] = "A senha RCON é uma configuração operacional do painel.",
        ["DefaultPort"] = "A porta do jogo é uma configuração operacional protegida.",
        ["UDPPort"] = "A porta UDP é uma configuração operacional protegida."
    };

    private static readonly (string Group, string[] Prefixes)[] Groups =
    [
        ("Acesso e segurança", ["Open", "Password", "RCON", "Steam", "AntiCheat", "Ban", "Deny", "Login", "DoLua", "MaxAccounts", "DropOff", "HideAdmin"]),
        ("Jogadores", ["Player", "MaxPlayer", "Display", "ShowFirst", "Username", "HideDisguised", "Spawn", "Sleep", "Voice", "Chat", "GlobalChat", "Faction"]),
        ("PvP", ["PVP", "Safety", "ShowSafety", "War"]),
        ("Mundo e loot", ["Map", "Loot", "Safehouse", "SafeHouse", "NoFire", "Announce", "Sledgehammer", "AllowDestruction", "SaveWorld", "Item", "Vehicle", "Zombie", "Chopper", "Blood", "Trash", "Seed"]),
        ("Rede", ["DefaultPort", "UDPPort", "UPnP", "Ping", "MaxPackets", "Public", "server_browser"]),
        ("Backups", ["Backup"]),
        ("Discord", ["Discord", "Webhook"])
    ];

    private static readonly HashSet<string> GeneralKeys = new(StringComparer.Ordinal)
    {
        "PublicName", "PublicDescription", "ServerWelcomeMessage", "PauseEmpty", "ResetID", "ServerPlayerID", "Mods", "WorkshopItems"
    };

    public static IReadOnlyList<ServerSetting> Create(ServerIniDocument document) =>
        document.Keys.Select(key => CreateSetting(document, key)).ToArray();

    public static ServerSettingValueType InferType(string value)
    {
        if (bool.TryParse(value, out _)) return ServerSettingValueType.Boolean;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return ServerSettingValueType.Integer;
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return ServerSettingValueType.Decimal;
        return ServerSettingValueType.Text;
    }

    public static ServerSettingConstraints ParseConstraints(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return new(null, null, null);
        var minimumMatch = MinimumPattern().Match(description);
        var maximumMatch = MaximumPattern().Match(description);
        decimal? minimum = TryDecimal(minimumMatch.Groups["value"].Value);
        decimal? maximum = TryDecimal(maximumMatch.Groups["value"].Value);
        var defaultMatch = DefaultPattern().Match(description);
        return new(minimum, maximum, defaultMatch.Success ? defaultMatch.Groups["default"].Value : null);
    }

    public static bool IsProtected(string key, out string? reason) => ProtectedKeys.TryGetValue(key, out reason);

    public static void ValidateValue(ServerSetting setting, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\r') || value.Contains('\n')) throw new ArgumentException("O valor não pode conter quebra de linha.", setting.Key);

        decimal? numericValue = setting.ValueType switch
        {
            ServerSettingValueType.Boolean when !bool.TryParse(value, out _) => throw new ArgumentException("Use true ou false.", setting.Key),
            ServerSettingValueType.Integer when !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) => throw new ArgumentException("Informe um número inteiro válido.", setting.Key),
            ServerSettingValueType.Integer => decimal.Parse(value, CultureInfo.InvariantCulture),
            ServerSettingValueType.Decimal when !decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => throw new ArgumentException("Informe um número decimal válido usando ponto.", setting.Key),
            ServerSettingValueType.Decimal => decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
            _ => null
        };

        if (numericValue < setting.Constraints.Minimum || numericValue > setting.Constraints.Maximum)
        {
            throw new ArgumentOutOfRangeException(setting.Key, $"O valor deve ficar entre {setting.Constraints.Minimum} e {setting.Constraints.Maximum}.");
        }
    }

    private static ServerSetting CreateSetting(ServerIniDocument document, string key)
    {
        var value = document.Get(key) ?? string.Empty;
        var description = document.GetDescription(key);
        var readOnly = IsProtected(key, out var reason);
        return new(key, value, description, GetGroup(key), InferType(value), ParseConstraints(description), ServerIniDocument.IsSensitive(key), readOnly, reason);
    }

    private static string GetGroup(string key)
    {
        if (GeneralKeys.Contains(key)) return "Geral";
        foreach (var (group, prefixes) in Groups)
        {
            if (prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return group;
        }
        return "Outros";
    }

    private static decimal? TryDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    [GeneratedRegex(@"Min:\s*(?<value>-?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex MinimumPattern();

    [GeneratedRegex(@"Max:\s*(?<value>-?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex MaximumPattern();

    [GeneratedRegex(@"Default:\s*(?<default>[^\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex DefaultPattern();
}
