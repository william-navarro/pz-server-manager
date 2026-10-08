using System.Globalization;
using System.Text.Json;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Mods;

namespace PzServerManager.Infrastructure.Steam;

public sealed class SteamWorkshopMetadataClient : IWorkshopMetadataClient, IDisposable
{
    private const string Endpoint = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public SteamWorkshopMetadataClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(15) }, true)
    {
    }

    public SteamWorkshopMetadataClient(HttpClient httpClient)
        : this(httpClient, false)
    {
    }

    private SteamWorkshopMetadataClient(HttpClient httpClient, bool ownsClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = ownsClient;
    }

    public async Task<IReadOnlyDictionary<string, WorkshopItemMetadata>> GetAsync(
        IReadOnlyList<string> itemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        var normalized = itemIds.Select(WorkshopIdParser.Parse).Distinct(StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, WorkshopItemMetadata>(StringComparer.Ordinal);

        foreach (var batch in normalized.Chunk(100))
        {
            var form = new List<KeyValuePair<string, string>>
            {
                new("itemcount", batch.Length.ToString(CultureInfo.InvariantCulture))
            };
            form.AddRange(batch.Select((id, index) =>
                new KeyValuePair<string, string>($"publishedfileids[{index}]", id)));
            using var response = await _httpClient.PostAsync(Endpoint, new FormUrlEncodedContent(form), cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!json.RootElement.TryGetProperty("response", out var responseNode) ||
                !responseNode.TryGetProperty("publishedfiledetails", out var details)) continue;

            foreach (var detail in details.EnumerateArray())
            {
                var id = ReadString(detail, "publishedfileid");
                if (id is null || ReadInt64(detail, "result") is not 1) continue;
                var timestamp = ReadInt64(detail, "time_updated");
                var tags = detail.TryGetProperty("tags", out var tagNodes)
                    ? tagNodes.EnumerateArray().Select(tag => ReadString(tag, "tag")).Where(tag => tag is not null).Select(tag => tag!).ToArray()
                    : [];
                result[id] = new(
                    id,
                    ReadString(detail, "title") ?? id,
                    timestamp is > 0 ? DateTimeOffset.FromUnixTimeSeconds(timestamp.Value) : null,
                    ReadInt64(detail, "file_size"),
                    tags);
            }
        }

        return result;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private static long? ReadInt64(JsonElement element, string name)
    {
        var value = ReadString(element, name);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }
}
