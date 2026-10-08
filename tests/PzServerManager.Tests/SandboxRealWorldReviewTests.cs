using System.Globalization;
using System.Text.RegularExpressions;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.Configuration;
using PzServerManager.Application.Mods;
using PzServerManager.Infrastructure.Configuration;

namespace PzServerManager.Tests;

public sealed class SandboxSyntheticReviewTests
{
    [Fact]
    public void SyntheticB42File_AllOptionsHaveUniquePathsAndCoherentMetadata()
    {
        var text = File.ReadAllText(RealSandboxFixture);
        var expected = ParseFixtureIndependently(text);
        var document = SandboxVarsDocument.Parse(text);
        var entries = document.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var failures = new List<string>();

        Check(expected.Count == entries.Count,
            $"quantidade: fixture={expected.Count}, parser={entries.Count}", failures);
        Check(document.Entries.Select(entry => entry.Path).Distinct(StringComparer.Ordinal).Count() == document.Entries.Count,
            "há caminhos completos duplicados", failures);

        foreach (var option in expected)
        {
            if (!entries.TryGetValue(option.Path, out var entry))
            {
                failures.Add($"{option.Path}: não foi carregada");
                continue;
            }

            Check(entry.ValueType == DetectType(option.RawValue),
                $"{option.Path}: tipo {entry.ValueType} não corresponde ao literal {option.RawValue}", failures);
            Check(entry.Description == option.Description,
                $"{option.Path}: descrição pertence a outro campo ou comentário", failures);
            Check(entry.EnumOptions.SequenceEqual(option.EnumOptions),
                $"{option.Path}: lista de enumeração não corresponde aos comentários", failures);
            Check(entry.Minimum == option.Minimum && entry.Maximum == option.Maximum,
                $"{option.Path}: limites não correspondem aos comentários", failures);

            if (option.EnumOptions.Count > 0)
            {
                Check(entry.ValueType == SandboxValueType.Integer,
                    $"{option.Path}: comentários de enumeração exigem inteiro", failures);
                Check(int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var current) &&
                      option.EnumOptions.Any(item => item.Value == current),
                    $"{option.Path}: valor atual {entry.Value} não existe na enumeração", failures);
            }

            if (option.Minimum is not null || option.Maximum is not null)
            {
                Check(entry.EnumOptions.Count == 0,
                    $"{option.Path}: limites numéricos foram tratados como enumeração", failures);
                Check(decimal.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var current) &&
                      (option.Minimum is null || current >= option.Minimum) &&
                      (option.Maximum is null || current <= option.Maximum),
                    $"{option.Path}: valor atual {entry.Value} fora dos limites", failures);
            }

            if (option.DefaultValue is not null)
            {
                Check(entry.DefaultValue == option.DefaultValue,
                    $"{option.Path}: padrão esperado {option.DefaultValue}, obtido {entry.DefaultValue ?? "nulo"}", failures);
                Check(DefaultIsValid(entry),
                    $"{option.Path}: padrão {entry.DefaultValue ?? "nulo"} incoerente com tipo/opções/limites", failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task SyntheticB42Ui_ResolvesCollidingKeysEnumsModifiersAndDescriptionsByPath()
    {
        using var environment = new RealSandboxEnvironment();
        var snapshot = await environment.CreateService().LoadAsync(
            environment.SandboxPath, environment.IniPath, environment.InstallPath, "42.21");

        Assert.Equal(snapshot.Options.Count,
            snapshot.Options.Select(option => option.Path).Distinct(StringComparer.Ordinal).Count());
        AssertDisplayedDescriptionsBelongToTheirFields(snapshot);

        var farmingSpeed = Option(snapshot, "Farming");
        var farmingMultiplier = Option(snapshot, "MultiplierConfig.Farming");
        Assert.Equal("Velocidade de Cultivo", farmingSpeed.Label);
        Assert.Contains("crescimento das plantas", farmingSpeed.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, farmingSpeed.EnumOptions.Count);
        Assert.Equal("Normal", farmingSpeed.DisplayDefaultValue);
        Assert.Equal("Multiplicador de Agricultura", farmingMultiplier.Label);
        Assert.Contains("evolução em Agricultura", farmingMultiplier.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(farmingMultiplier.EnumOptions);

        var zombieStrength = Option(snapshot, "ZombieLore.Strength");
        var skillStrength = Option(snapshot, "MultiplierConfig.Strength");
        Assert.Equal("Força", zombieStrength.Label);
        Assert.Equal("Multiplicador de Força", skillStrength.Label);
        Assert.NotEqual(zombieStrength.Description, skillStrength.Description);

        var cleanupLimit = Option(snapshot, "ZombieConfig.ZombiesCountBeforeDelete");
        Assert.Contains("cleanup", cleanupLimit.Description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("occupy a certain area", cleanupLimit.Description, StringComparison.OrdinalIgnoreCase);

        var basementFrequency = Option(snapshot, "Basement.SpawnFrequency");
        Assert.Equal("Frequência de Geração de Porões", basementFrequency.Label);
        Assert.Equal("Frequente", basementFrequency.EnumOptions.Single(item => item.Value == 5).Label);

        AssertDayPair(snapshot, "WaterShut", "WaterShutModifier", "0 a 30 Dias");
        AssertDayPair(snapshot, "ElecShut", "ElecShutModifier", "14 a 30 Dias");
        AssertDayPair(snapshot, "AlarmDecay", "AlarmDecayModifier", "0 a 30 Dias");

        AssertEnumLabel(snapshot, "PlantAbundance", 1, "Muito Escassa");
        AssertEnumLabel(snapshot, "Helicopter", 2, "Uma vez");
        AssertEnumLabel(snapshot, "MetaEvent", 2, "Às vezes");
        AssertEnumLabel(snapshot, "SleepingEvent", 2, "Às vezes");
        AssertEnumLabel(snapshot, "VehicleStoryChance", 3, "Rara");
        AssertEnumLabel(snapshot, "ZoneStoryChance", 3, "Rara");
        foreach (var path in new[]
                 {
                     "AnimalStatsModifier", "AnimalMetaStatsModifier", "AnimalPregnancyTime", "AnimalAgeModifier",
                     "AnimalMilkIncModifier", "AnimalWoolIncModifier", "AnimalEggHatch"
                 })
            AssertEnumLabel(snapshot, path, 1, "Ultrarrápida");
        AssertEnumLabel(snapshot, "AnimalTrackChance", 2, "Extremamente Rara");
        AssertEnumLabel(snapshot, "AnimalPathChance", 2, "Extremamente Rara");
    }

    private static SandboxOptionSetting Option(SandboxSettingsSnapshot snapshot, string path) =>
        Assert.Single(snapshot.Options, option => option.Path == path);

    private static void AssertDayPair(
        SandboxSettingsSnapshot snapshot,
        string enumPath,
        string modifierPath,
        string expectedDefault)
    {
        var range = Option(snapshot, enumPath);
        var days = Option(snapshot, modifierPath);
        Assert.NotEmpty(range.EnumOptions);
        Assert.Null(range.Minimum);
        Assert.Contains("faixa predefinida", range.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedDefault, range.DisplayDefaultValue);
        Assert.Empty(days.EnumOptions);
        Assert.NotNull(days.Minimum);
        Assert.Contains("dias efetivos", days.Label, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valor efetivo em dias", days.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(modifierPath, range.Description, StringComparison.Ordinal);
    }

    private static void AssertEnumLabel(
        SandboxSettingsSnapshot snapshot,
        string path,
        int value,
        string expectedLabel) =>
        Assert.Equal(expectedLabel, Option(snapshot, path).EnumOptions.Single(option => option.Value == value).Label);

    private static void AssertDisplayedDescriptionsBelongToTheirFields(SandboxSettingsSnapshot snapshot)
    {
        var document = SandboxVarsDocument.Parse(File.ReadAllText(RealSandboxFixture));
        var english = SandboxTranslationCatalog.ParseJson(File.ReadAllText(
            Path.Combine(Fixtures, "sandbox", "vanilla-Sandbox-EN.json")));
        var ptBr = SandboxTranslationCatalog.ParseJson(File.ReadAllText(
            Path.Combine(Fixtures, "sandbox", "vanilla-Sandbox-PTBR.json")));
        var translatedDescriptions = english
            .Where(pair => pair.Key.EndsWith("_tooltip", StringComparison.Ordinal) ||
                           pair.Key.EndsWith("_help", StringComparison.Ordinal))
            .ToArray();
        var failures = new List<string>();

        foreach (var option in snapshot.Options.Where(option => option.SourceKey == "__base"))
        {
            var entry = Assert.Single(document.Entries, entry => entry.Path == option.Path);
            if (entry.Description is null || option.Description is null) continue;
            var source = NormalizeDescription(entry.Description);
            var displayed = NormalizeDescription(option.Description);
            if (displayed.Contains(source, StringComparison.Ordinal)) continue;

            var matchingKeys = translatedDescriptions
                .Where(pair => DescriptionsCorrespond(source, NormalizeDescription(pair.Value)))
                .Select(pair => pair.Key)
                .ToArray();
            var belongsToField = matchingKeys.Any(key =>
                ptBr.TryGetValue(key, out var translated) &&
                displayed.Contains(NormalizeDescription(translated), StringComparison.Ordinal));
            if (!belongsToField)
                failures.Add($"{option.Path}: descrição exibida não corresponde ao comentário da própria opção");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static bool DescriptionsCorrespond(string left, string right) =>
        left.Length == 0 || right.Length == 0 ||
        left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal);

    private static string NormalizeDescription(string value)
    {
        var withoutMetadata = Regex.Replace(value,
            @"(?:Min:\s*[-+]?\d+(?:\.\d+)?\s+Max:\s*[-+]?\d+(?:\.\d+)?\s+Default:\s*[-+]?\d+(?:\.\d+)?|Default\s*=\s*.+)$",
            string.Empty, RegexOptions.IgnoreCase);
        return Regex.Replace(withoutMetadata.Replace("%%", "%", StringComparison.Ordinal).ToLowerInvariant(),
            @"[^\p{L}\p{N}%]+", " ").Trim();
    }

    private static bool DefaultIsValid(SandboxVarsEntry entry)
    {
        if (entry.DefaultValue is null) return true;
        if (entry.ValueType == SandboxValueType.Boolean)
            return bool.TryParse(entry.DefaultValue, out _);
        if (entry.ValueType == SandboxValueType.String)
            return true;
        if (!decimal.TryParse(entry.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return false;
        if (entry.Minimum is not null && number < entry.Minimum || entry.Maximum is not null && number > entry.Maximum)
            return false;
        return entry.EnumOptions.Count == 0 || entry.EnumOptions.Any(option => option.Value == number);
    }

    private static SandboxValueType DetectType(string raw) => raw switch
    {
        "true" or "false" => SandboxValueType.Boolean,
        _ when Regex.IsMatch(raw, @"^[-+]?\d+$") => SandboxValueType.Integer,
        _ when Regex.IsMatch(raw, @"^[-+]?\d+\.\d+$") => SandboxValueType.Double,
        _ => SandboxValueType.String
    };

    private static IReadOnlyList<ExpectedOption> ParseFixtureIndependently(string text)
    {
        var results = new List<ExpectedOption>();
        var comments = new List<string>();
        string? group = null;
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0) { comments.Clear(); continue; }
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
            {
                comments.Add(trimmed[2..].TrimStart());
                continue;
            }

            var groupMatch = Regex.Match(rawLine, @"^    (?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\{$");
            if (groupMatch.Success)
            {
                group = groupMatch.Groups["name"].Value;
                comments.Clear();
                continue;
            }
            if (group is not null && Regex.IsMatch(rawLine, @"^    \},?$"))
            {
                group = null;
                comments.Clear();
                continue;
            }

            var assignment = Regex.Match(rawLine,
                "^\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(?<value>true|false|[-+]?\\d+(?:\\.\\d+)?|\"(?:\\\\.|[^\"\\\\])*\")\\s*,\\s*$");
            if (!assignment.Success) continue;
            var name = assignment.Groups["name"].Value;
            var path = group is null ? name : $"{group}.{name}";
            results.Add(CreateExpected(path, assignment.Groups["value"].Value, comments));
            comments.Clear();
        }
        return results;
    }

    private static ExpectedOption CreateExpected(string path, string rawValue, IReadOnlyList<string> comments)
    {
        var enumOptions = new List<SandboxEnumOption>();
        var descriptions = new List<string>();
        decimal? minimum = null, maximum = null;
        string? defaultValue = null;
        foreach (var comment in comments)
        {
            var enumMatch = Regex.Match(comment, @"^\s*(?<value>-?\d+)\s*=\s*(?<label>.+?)\s*$");
            if (enumMatch.Success)
            {
                enumOptions.Add(new(int.Parse(enumMatch.Groups["value"].Value, CultureInfo.InvariantCulture),
                    enumMatch.Groups["label"].Value.Trim()));
                continue;
            }

            var constraints = Regex.Match(comment,
                @"Min:\s*(?<min>[-+]?\d+(?:\.\d+)?)\s+Max:\s*(?<max>[-+]?\d+(?:\.\d+)?)\s+Default:\s*(?<default>[-+]?\d+(?:\.\d+)?)",
                RegexOptions.IgnoreCase);
            if (constraints.Success)
            {
                minimum = decimal.Parse(constraints.Groups["min"].Value, CultureInfo.InvariantCulture);
                maximum = decimal.Parse(constraints.Groups["max"].Value, CultureInfo.InvariantCulture);
                defaultValue = constraints.Groups["default"].Value;
                var description = comment.Remove(constraints.Index, constraints.Length).Trim();
                if (description.Length > 0) descriptions.Add(description);
                continue;
            }

            descriptions.Add(comment);
            var namedDefault = Regex.Match(comment, @"Default\s*=\s*(?<default>.+?)\s*$", RegexOptions.IgnoreCase);
            if (namedDefault.Success) defaultValue = namedDefault.Groups["default"].Value.Trim();
        }

        if (defaultValue is not null && enumOptions.Count > 0)
        {
            var matchingOption = enumOptions.FirstOrDefault(option =>
                option.Label.Equals(defaultValue, StringComparison.OrdinalIgnoreCase));
            if (matchingOption is not null)
                defaultValue = matchingOption.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (defaultValue is not null && DetectType(rawValue) == SandboxValueType.Boolean)
            defaultValue = defaultValue.ToLowerInvariant();

        return new(path, rawValue, descriptions.Count == 0 ? null : string.Join('\n', descriptions),
            enumOptions, minimum, maximum, defaultValue);
    }

    private static void Check(bool condition, string failure, ICollection<string> failures)
    {
        if (!condition) failures.Add(failure);
    }

    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static string RealSandboxFixture => Path.Combine(Fixtures, "sandbox-review", "servertest_SandboxVars.lua");

    private sealed record ExpectedOption(
        string Path,
        string RawValue,
        string? Description,
        IReadOnlyList<SandboxEnumOption> EnumOptions,
        decimal? Minimum,
        decimal? Maximum,
        string? DefaultValue);

    private sealed class RealSandboxEnvironment : IDisposable
    {
        public RealSandboxEnvironment()
        {
            Root = Path.Combine(Path.GetTempPath(), $"PzSandboxRealReview-{Guid.NewGuid():N}");
            InstallPath = Path.Combine(Root, "server");
            SandboxPath = Path.Combine(Root, "servertest_SandboxVars.lua");
            IniPath = Path.Combine(Root, "servertest.ini");
            Directory.CreateDirectory(Path.Combine(InstallPath, "media", "lua", "shared", "Translate", "EN"));
            Directory.CreateDirectory(Path.Combine(InstallPath, "media", "lua", "shared", "Translate", "PTBR"));
            File.Copy(RealSandboxFixture, SandboxPath);
            File.WriteAllText(IniPath, "Mods=\nWorkshopItems=\n");
            File.Copy(Path.Combine(Fixtures, "sandbox", "vanilla-Sandbox-EN.json"),
                Path.Combine(InstallPath, "media", "lua", "shared", "Translate", "EN", "Sandbox.json"));
            File.Copy(Path.Combine(Fixtures, "sandbox", "vanilla-Sandbox-PTBR.json"),
                Path.Combine(InstallPath, "media", "lua", "shared", "Translate", "PTBR", "Sandbox.json"));
        }

        public string Root { get; }
        public string InstallPath { get; }
        public string SandboxPath { get; }
        public string IniPath { get; }

        public SandboxSettingsService CreateService() => new(
            new SandboxVarsFileStore(), new ServerIniFileStore(), new NoOpAuditLog(), new WorkshopItemInspector());

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class NoOpAuditLog : IAuditLog
    {
        public Task WriteAsync(string userName, string action, string result, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
