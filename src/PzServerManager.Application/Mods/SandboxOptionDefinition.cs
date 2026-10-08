using PzServerManager.Application.Configuration;

namespace PzServerManager.Application.Mods;

public sealed record SandboxOptionDefinition(
    string Path,
    SandboxValueType ValueType,
    string DefaultValue,
    decimal? Minimum,
    decimal? Maximum,
    int? NumberOfValues,
    string? Page,
    string? Translation,
    string? TooltipTranslation,
    string? ValueTranslation);

public sealed record TranslatedSandboxOption(
    string Label,
    string? Tooltip,
    string Page,
    IReadOnlyList<SandboxEnumOption> EnumOptions);
