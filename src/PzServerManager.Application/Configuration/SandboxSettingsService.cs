using System.Globalization;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Mods;

namespace PzServerManager.Application.Configuration;

public sealed record SandboxOptionSetting(
    string Path,
    string Label,
    string? Description,
    string SourceKey,
    string SourceLabel,
    string Section,
    SandboxValueType ValueType,
    string Value,
    string? DefaultValue,
    decimal? Minimum,
    decimal? Maximum,
    IReadOnlyList<SandboxEnumOption> EnumOptions,
    bool IsNewWorldOnly)
{
    public string? DisplayDefaultValue
    {
        get
        {
            if (DefaultValue is null) return null;
            if (!int.TryParse(DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return DefaultValue;
            return EnumOptions.FirstOrDefault(option => option.Value == value)?.Label ?? DefaultValue;
        }
    }

    public bool IsChangedFromDefault => DefaultValue is not null && !Equivalent(Value, DefaultValue);

    private bool Equivalent(string left, string right)
    {
        if (ValueType is SandboxValueType.Integer or SandboxValueType.Double &&
            decimal.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
            decimal.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)) return a == b;
        return string.Equals(left, right, StringComparison.Ordinal);
    }
}

public sealed record SandboxSettingsSnapshot(
    string OriginalText,
    IReadOnlyList<SandboxOptionSetting> Options,
    IReadOnlyList<string> Warnings);

public sealed record SandboxSettingChange(string Path, string Before, string After);

public sealed partial class SandboxSettingsService(
    ISandboxVarsStore sandboxStore,
    IServerIniStore iniStore,
    IAuditLog auditLog,
    WorkshopItemInspector inspector)
{
    private const string BaseSource = "__base";
    private static readonly HashSet<string> BuiltInGroups =
        ["Basement", "Map", "ZombieLore", "ZombieConfig", "MultiplierConfig"];
    private static readonly IReadOnlyDictionary<string, string> VanillaTranslationAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Zombies"] = "ZombieCount",
            ["Distribution"] = "ZombieDistribution",
            ["Farming"] = "FarmingSpeed",
            ["Temperature"] = "WorldTemperature",
            ["Rain"] = "RainAmount",
            ["StatsDecrease"] = "StatDecrease",
            ["NatureAbundance"] = "NatureAmount",
            ["Alarm"] = "HouseAlarmFrequency",
            ["LockedHouses"] = "LockedHouseFrequency",
            ["FoodRotSpeed"] = "FoodSpoil",
            ["FridgeFactor"] = "FridgeEffect",
            ["EndRegen"] = "EnduranceRegen",
            ["CarAlarm"] = "CarAlarmFrequency",
            ["FishAbundance"] = "FishAmount",
            ["ZombieLore.Mortality"] = "ZInfectionMortality",
            ["ZombieLore.Reanimate"] = "ZReanimateTime",
            ["ZombieLore.PlayerSpawnZombieRemoval"] = "ZSpawnRemoval"
        };
    private static readonly IReadOnlyDictionary<string, string> VanillaValueTranslationAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WaterShut"] = "Shutoff",
            ["AlarmDecay"] = "Shutoff",
            ["PlantAbundance"] = "FarmingAmount",
            ["Helicopter"] = "HelicopterFreq",
            ["MetaEvent"] = "MetaEventFreq",
            ["SleepingEvent"] = "MetaEventFreq",
            ["VehicleStoryChance"] = "SurvivorHouseChance",
            ["ZoneStoryChance"] = "SurvivorHouseChance",
            ["AnimalStatsModifier"] = "AnimalSpeed",
            ["AnimalMetaStatsModifier"] = "AnimalSpeed",
            ["AnimalPregnancyTime"] = "AnimalSpeed",
            ["AnimalAgeModifier"] = "AnimalSpeed",
            ["AnimalMilkIncModifier"] = "AnimalSpeed",
            ["AnimalWoolIncModifier"] = "AnimalSpeed",
            ["AnimalEggHatch"] = "AnimalSpeed",
            ["AnimalTrackChance"] = "AnimalRanchChance",
            ["AnimalPathChance"] = "AnimalRanchChance"
        };

    // O jogo lê estas opções durante a criação do save; mudá-las em um mundo existente não reescreve seu estado inicial.
    public static IReadOnlySet<string> NewWorldOnlyOptions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Zombies", "Distribution", "WaterShut", "ElecShut",
        "StartYear", "StartMonth", "StartDay", "StartTime", "TimeSinceApo"
    };

    public async Task<SandboxSettingsSnapshot> LoadAsync(
        string sandboxPath,
        string iniPath,
        string installPath,
        string serverVersion,
        CancellationToken cancellationToken = default)
    {
        var document = await sandboxStore.LoadAsync(sandboxPath, cancellationToken);
        var ini = await iniStore.LoadAsync(iniPath, cancellationToken);
        var warnings = new List<string>();
        var result = new List<SandboxOptionSetting>();

        var vanilla = SandboxTranslationCatalog.LoadVanilla(installPath);
        foreach (var entry in document.Entries.Where(IsBaseGameEntry))
            result.Add(CreateBaseOption(entry, vanilla));

        var activeModIds = ini.GetList("Mods");
        var workshopItems = ini.GetList("WorkshopItems");
        var workshopRoot = Path.Combine(installPath, "steamapps", "workshop", "content", "108600");
        var inspected = new List<(string ItemId, InspectedMod Mod)>();
        foreach (var itemId in workshopItems)
        {
            var itemPath = Path.Combine(workshopRoot, itemId);
            if (!Directory.Exists(itemPath))
            {
                warnings.Add($"Item da Workshop {itemId} não encontrado em {workshopRoot}.");
                continue;
            }

            try
            {
                var item = inspector.Inspect(itemId, itemPath, serverVersion);
                inspected.AddRange(item.Mods.Select(mod => (itemId, mod)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                warnings.Add($"Não foi possível inspecionar o item {itemId}: {exception.Message}");
            }
        }

        foreach (var modId in activeModIds)
        {
            var match = inspected.FirstOrDefault(item => item.Mod.Id.Equals(modId, StringComparison.Ordinal));
            if (match.Mod is null)
            {
                warnings.Add($"O mod ativo {modId} não foi localizado nos itens da Workshop configurados.");
                continue;
            }

            var contentRoot = match.Mod.ContentPath;
            if (string.IsNullOrWhiteSpace(contentRoot)) continue;
            var optionsPath = Path.Combine(contentRoot, "media", "sandbox-options.txt");
            if (!File.Exists(optionsPath)) continue;

            try
            {
                var definitions = SandboxOptionsParser.Parse(await File.ReadAllTextAsync(optionsPath, cancellationToken));
                var translations = SandboxTranslationCatalog.LoadFromContentRoot(contentRoot);
                foreach (var definition in definitions)
                {
                    var entry = document.Entries.FirstOrDefault(item => item.Path.Equals(definition.Path, StringComparison.Ordinal));
                    if (entry is null) continue;
                    result.RemoveAll(option => option.SourceKey == BaseSource && option.Path.Equals(definition.Path, StringComparison.Ordinal));
                    if (result.Any(option => option.Path.Equals(definition.Path, StringComparison.Ordinal)))
                    {
                        warnings.Add($"A opção {definition.Path} foi declarada por mais de um mod ativo; foi mantida a primeira definição.");
                        continue;
                    }
                    var translated = translations.Translate(definition);
                    result.Add(new(
                        definition.Path, translated.Label, translated.Tooltip ?? entry.Description,
                        modId, match.Mod.Name, translated.Page, definition.ValueType, entry.Value,
                        definition.DefaultValue, definition.Minimum, definition.Maximum,
                        translated.EnumOptions, false));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
            {
                warnings.Add($"Não foi possível carregar as opções de {match.Mod.Name}: {exception.Message}");
            }
        }

        return new(document.ToText(), result, warnings);
    }

    public async Task<IReadOnlyList<SandboxSettingChange>> SaveAsync(
        string sandboxPath,
        SandboxSettingsSnapshot snapshot,
        IReadOnlyDictionary<string, string> values,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(values);
        var current = await sandboxStore.LoadAsync(sandboxPath, cancellationToken);
        if (!string.Equals(current.ToText(), snapshot.OriginalText, StringComparison.Ordinal))
            throw new ServerSettingsConflictException("O arquivo de opções do mundo foi alterado no disco. Recarregue a página antes de salvar.");

        var known = snapshot.Options.ToDictionary(option => option.Path, StringComparer.Ordinal);
        var changes = new List<SandboxSettingChange>();
        foreach (var (path, value) in values)
        {
            if (!known.TryGetValue(path, out var option))
                throw new ArgumentException($"A opção de mundo '{path}' não existe na tela aberta.", nameof(values));
            if (string.Equals(option.Value, value, StringComparison.Ordinal)) continue;
            ValidateValue(option, value);
            current.Set(path, value);
            changes.Add(new(path, option.Value, value));
        }

        if (changes.Count == 0) return changes;
        await sandboxStore.SaveAsync(sandboxPath, current, cancellationToken);
        foreach (var change in changes)
            await auditLog.WriteAsync(actor, "alterar opção de mundo",
                $"{change.Path}: {change.Before} -> {change.After}", CancellationToken.None);
        return changes;
    }

    public static void ValidateValue(SandboxOptionSetting option, string value)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(value);
        decimal? number = null;
        switch (option.ValueType)
        {
            case SandboxValueType.Boolean:
                if (!bool.TryParse(value, out _)) throw Invalid(option, "use true ou false");
                break;
            case SandboxValueType.Integer:
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                    throw Invalid(option, "use um número inteiro");
                number = integer;
                break;
            case SandboxValueType.Double:
                if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    throw Invalid(option, "use um número decimal com ponto");
                number = parsed;
                break;
        }
        if (number is not null && option.Minimum is not null && number < option.Minimum)
            throw Invalid(option, $"o mínimo é {option.Minimum.Value.ToString(CultureInfo.InvariantCulture)}");
        if (number is not null && option.Maximum is not null && number > option.Maximum)
            throw Invalid(option, $"o máximo é {option.Maximum.Value.ToString(CultureInfo.InvariantCulture)}");
        if (option.EnumOptions.Count > 0 &&
            (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var selected) ||
             option.EnumOptions.All(item => item.Value != selected)))
            throw Invalid(option, "selecione um valor da lista");
    }

    private static ArgumentException Invalid(SandboxOptionSetting option, string detail) =>
        new($"Valor inválido para '{option.Path}': {detail}.");

    private static bool IsBaseGameEntry(SandboxVarsEntry entry) =>
        !entry.Path.Equals("VERSION", StringComparison.Ordinal) &&
        (entry.Group is null || BuiltInGroups.Contains(entry.Group));

    private static SandboxOptionSetting CreateBaseOption(
        SandboxVarsEntry entry,
        SandboxTranslationCatalog translations)
    {
        var stems = VanillaTranslationStems(entry).ToArray();
        var label = stems.Select(translations.Get).FirstOrDefault(value => value is not null) ?? entry.Name;
        var descriptionKey = stems.SelectMany(stem => new[] { $"{stem}_tooltip", $"{stem}_help" })
            .FirstOrDefault(key => translations.Get(key) is not null);
        var description = descriptionKey is null ? entry.Description : translations.Get(descriptionKey);
        if (entry.Description is not null && descriptionKey is not null &&
            translations.GetEnglish(descriptionKey) is { } englishDescription &&
            !DescriptionsCorrespond(entry.Description, englishDescription))
        {
            description = entry.Description;
        }
        var valueStems = VanillaValueTranslationStems(entry, stems).ToArray();
        var enums = entry.EnumOptions.Select(option => new SandboxEnumOption(
            option.Value,
            valueStems.Select(stem => translations.Get($"{stem}_option{option.Value}"))
                .FirstOrDefault(value => value is not null) ?? option.Label)).ToArray();
        (label, description) = ClarifyDayPair(entry.Path, label, description, translations);
        return new(entry.Path, label, description, BaseSource, "Jogo base", BaseSection(entry),
            entry.ValueType, entry.Value, entry.DefaultValue, entry.Minimum, entry.Maximum, enums,
            NewWorldOnlyOptions.Contains(entry.Path));
    }

    private static IEnumerable<string> VanillaTranslationStems(SandboxVarsEntry entry)
    {
        if (VanillaTranslationAliases.TryGetValue(entry.Path, out var alias)) yield return alias;
        if (entry.Group == "ZombieLore") yield return $"Z{entry.Name}";
        if (entry.Group == "Basement") yield return $"Basement{entry.Name}";
        yield return entry.Name;
        if (entry.Group is not null) yield return $"{entry.Group}.{entry.Name}";
    }

    private static IEnumerable<string> VanillaValueTranslationStems(
        SandboxVarsEntry entry,
        IReadOnlyList<string> labelStems)
    {
        if (VanillaValueTranslationAliases.TryGetValue(entry.Path, out var alias)) yield return alias;
        foreach (var stem in labelStems) yield return stem;
    }

    private static (string Label, string? Description) ClarifyDayPair(
        string path,
        string label,
        string? description,
        SandboxTranslationCatalog translations)
    {
        var modifierPath = path switch
        {
            "WaterShut" => "WaterShutModifier",
            "ElecShut" => "ElecShutModifier",
            "AlarmDecay" => "AlarmDecayModifier",
            _ => null
        };
        if (modifierPath is not null)
        {
            return ($"{label} — faixa predefinida", AppendDescription(description,
                $"Esta lista escolhe uma faixa. O valor efetivo em dias usado pelo servidor é {modifierPath}."));
        }

        var enumPath = path switch
        {
            "WaterShutModifier" => "WaterShut",
            "ElecShutModifier" => "ElecShut",
            "AlarmDecayModifier" => "AlarmDecay",
            _ => null
        };
        if (enumPath is null) return (label, description);

        var subject = translations.Get(enumPath) ?? label;
        return ($"{subject} — dias efetivos", AppendDescription(description,
            $"Este número é o valor efetivo em dias usado pelo servidor; {enumPath} escolhe apenas a faixa predefinida."));
    }

    private static string AppendDescription(string? description, string detail) =>
        string.IsNullOrWhiteSpace(description) ? detail : $"{description}\n{detail}";

    private static bool DescriptionsCorrespond(string comment, string translation)
    {
        var normalizedComment = NormalizeDescription(CommentMetadataRegex().Replace(comment, string.Empty));
        var normalizedTranslation = NormalizeDescription(translation);
        return normalizedComment.Length == 0 || normalizedTranslation.Length == 0 ||
               normalizedComment.Contains(normalizedTranslation, StringComparison.Ordinal) ||
               normalizedTranslation.Contains(normalizedComment, StringComparison.Ordinal);
    }

    private static string NormalizeDescription(string value) =>
        NonAlphaNumericRegex().Replace(value.Replace("%%", "%", StringComparison.Ordinal).ToLowerInvariant(), " ").Trim();

    [System.Text.RegularExpressions.GeneratedRegex(@"(?:Min:\s*[-+]?\d+(?:\.\d+)?\s+Max:\s*[-+]?\d+(?:\.\d+)?\s+Default:\s*[-+]?\d+(?:\.\d+)?|Default\s*=\s*.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex CommentMetadataRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"[^\p{L}\p{N}%]+", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex NonAlphaNumericRegex();

    private static string BaseSection(SandboxVarsEntry entry)
    {
        if (entry.Group is "ZombieLore" or "ZombieConfig" ||
            StartsWithAny(entry.Name, "Zombie", "Zombies", "Distribution")) return "Zumbis";
        if (entry.Group == "MultiplierConfig") return "Multiplicadores de XP";
        if (entry.Group is "Map" or "Basement") return "Mapa e porões";
        if (ContainsAny(entry.Name, "Loot", "Removal", "AnnotatedMap")) return "Loot";
        if (StartsWithAny(entry.Name, "Car", "Vehicle", "FuelStation", "Traffic", "Siren")) return "Veículos";
        if (StartsWithAny(entry.Name, "Animal", "Farming", "Plant", "Fish", "Nature", "Erosion", "Clay")) return "Natureza e animais";
        if (StartsWithAny(entry.Name, "Character", "Stats", "Nutrition", "Sleep", "Bone", "Injury", "Wound", "Blood", "Muscle", "Discomfort")) return "Personagem";
        if (StartsWithAny(entry.Name, "Day", "Start", "Climate", "Fog", "Water", "Elec", "AlarmDecay", "Temperature", "Rain", "TimeSinceApo", "Night", "Helicopter", "MetaEvent")) return "Tempo e mundo";
        return "Outros";
    }

    private static bool StartsWithAny(string value, params string[] prefixes) =>
        prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
    private static bool ContainsAny(string value, params string[] parts) =>
        parts.Any(part => value.Contains(part, StringComparison.Ordinal));
}
