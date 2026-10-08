namespace PzServerManager.Application.Configuration;

public enum ServerSettingValueType
{
    Boolean,
    Integer,
    Decimal,
    Text
}

public sealed record ServerSettingConstraints(decimal? Minimum, decimal? Maximum, string? DefaultValue);

public sealed record ServerSetting(
    string Key,
    string Value,
    string? Description,
    string Group,
    ServerSettingValueType ValueType,
    ServerSettingConstraints Constraints,
    bool IsSensitive,
    bool IsReadOnly,
    string? ReadOnlyReason);

public sealed record ServerSettingsSnapshot(string OriginalText, IReadOnlyList<ServerSetting> Settings);

public sealed record ServerSettingChange(string Key, string Before, string After, bool IsSensitive);
