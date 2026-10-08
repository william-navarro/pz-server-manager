namespace PzServerManager.Application.Updates;

public enum UpdateTarget { Mods, Game, All }

public sealed record OutdatedWorkshopItem(
    string ItemId,
    string Title,
    DateTimeOffset? InstalledAt,
    DateTimeOffset PublishedAt);

public sealed record UpdateHistoryEntry(
    DateTimeOffset Timestamp,
    UpdateTarget Target,
    string Description,
    string? BackupFileName,
    bool Succeeded,
    string Result);

public sealed record UpdateOverview(
    DateTimeOffset? LastCheckedAt,
    IReadOnlyList<OutdatedWorkshopItem> OutdatedMods,
    string? InstalledGameBuild,
    string? PublishedGameBuild,
    IReadOnlyList<UpdateHistoryEntry> History,
    string? LastError)
{
    public const string GameCheckErrorPrefix = "Jogo: ";

    /// <summary>A lista de mods veio da verificação atual (erro, se houver, foi só na consulta do build do jogo).</summary>
    public bool ModsCheckSucceeded => string.IsNullOrWhiteSpace(LastError) ||
        LastError.StartsWith(GameCheckErrorPrefix, StringComparison.Ordinal);

    /// <summary>Identifica o conjunto pendente; a automação não tenta duas vezes a mesma assinatura.</summary>
    public string PendingSignature => string.Join(";", OutdatedMods.OrderBy(mod => mod.ItemId, StringComparer.Ordinal)
        .Select(mod => $"{mod.ItemId}@{mod.PublishedAt.ToUnixTimeSeconds()}")) +
        (IsGameOutdated ? $"|jogo:{PublishedGameBuild}" : "");

    public bool IsGameOutdated => !string.IsNullOrWhiteSpace(InstalledGameBuild) &&
        !string.IsNullOrWhiteSpace(PublishedGameBuild) &&
        !string.Equals(InstalledGameBuild, PublishedGameBuild, StringComparison.Ordinal);
}
