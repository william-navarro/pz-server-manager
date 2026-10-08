using PzServerManager.Application.Configuration;
using PzServerManager.Application.Mods;

namespace PzServerManager.Tests;

public sealed class SandboxTranslationTests
{
    [Fact]
    public void JsonTranslations_PreferPtBrThenEnglishAndTranslateEnumAndPage()
    {
        var english = SandboxTranslationCatalog.ParseJson(File.ReadAllText(ALifeEnglish));
        var catalog = new SandboxTranslationCatalog(english, new Dictionary<string, string>
        {
            ["Sandbox_ExampleNpc_Mode"] = "Modo do diretor"
        });
        var definition = new SandboxOptionDefinition(
            "ExampleNpc.Mode", SandboxValueType.Integer, "2", null, null, 3,
            "ExampleNpc_Page", "ExampleNpc_Mode", "ExampleNpc_EncounterChance_tooltip", "ExampleNpc_Mode");

        var translated = catalog.Translate(definition);

        Assert.Equal("Modo do diretor", translated.Label);
        Assert.Contains("EXAMPLE NPC", translated.Page);
        Assert.Equal("Low", translated.EnumOptions[0].Label);
        Assert.NotNull(translated.Tooltip);
    }

    [Fact]
    public void LegacyTranslations_ParseSyntheticFilesAndPreferPtBr()
    {
        var english = SandboxTranslationCatalog.ParseLegacy(File.ReadAllText(MoreTraitsEnglish));
        var ptBr = SandboxTranslationCatalog.ParseLegacy(File.ReadAllText(MoreTraitsPtBr));
        var catalog = new SandboxTranslationCatalog(english, ptBr);

        Assert.Equal("Quantidade de bandagens", catalog.Get("ExampleTraits_BandageAmount"));
        Assert.NotNull(catalog.Get("ExampleTraits_Distance"));
    }

    [Fact]
    public void Translation_ReplacesEscapedPercentAndFallsBackToOptionName()
    {
        var values = SandboxTranslationCatalog.ParseJson("{\"Sandbox_Demo\":\"Chance 25%%\"}");
        var catalog = new SandboxTranslationCatalog(values, new Dictionary<string, string>());
        var missing = new SandboxOptionDefinition("Mod.RawName", SandboxValueType.Boolean, "true", null, null, null, null, "Missing", null, null);

        Assert.Equal("Chance 25%", catalog.Get("Demo"));
        Assert.Equal("RawName", catalog.Translate(missing).Label);
    }

    private static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures", "workshop");
    private static string ALifeEnglish => Path.Combine(Root, "1000000004", "mods", "Example NPC", "42.20", "media", "lua", "shared", "Translate", "EN", "Sandbox.json");
    private static string MoreTraitsRoot => Path.Combine(Root, "1000000001", "mods", "Example Traits", "42.20", "media", "lua", "shared", "Translate");
    private static string MoreTraitsEnglish => Path.Combine(MoreTraitsRoot, "EN", "Sandbox_EN.txt");
    private static string MoreTraitsPtBr => Path.Combine(MoreTraitsRoot, "PTBR", "Sandbox_PTBR.txt");
}
