using PzServerManager.Application.Configuration;

namespace PzServerManager.Tests;

public sealed class SandboxVarsDocumentTests
{
    [Fact]
    public void Parse_RoundTripsSyntheticFixtureExactly()
    {
        var text = ReadSandbox();
        var document = SandboxVarsDocument.Parse(text);

        Assert.Equal(text, document.ToText());
        Assert.False(document.HasChanges);
        Assert.True(document.Entries.Count > 25);
    }

    [Fact]
    public void GetAndSet_WorkAtRootAndInsideGroups_ChangingOnlyValueLines()
    {
        var original = ReadSandbox();
        var document = SandboxVarsDocument.Parse(original);

        Assert.Equal("4", document.Get("Zombies"));
        Assert.Equal("3.0", document.Get("MultiplierConfig.Global"));
        Assert.Equal("40", document.Get("ExampleNpc.EncounterChance"));

        document.Set("Zombies", "3");
        document.Set("MultiplierConfig.Global", "2.5");
        document.Set("ExampleNpc.EncounterChance", "60");

        var changed = original.Split('\n').Zip(document.ToText().Split('\n'))
            .Where(pair => pair.First != pair.Second).ToArray();
        Assert.Equal(3, changed.Length);
        Assert.Contains(changed, pair => pair.Second.Trim() == "Zombies = 3,");
        Assert.Contains(changed, pair => pair.Second.Trim() == "Global = 2.5,");
        Assert.Contains(changed, pair => pair.Second.Trim() == "EncounterChance = 60,");
    }

    [Fact]
    public void Entries_ExposeCommentsEnumsAndLimits()
    {
        var document = SandboxVarsDocument.Parse(ReadSandbox());
        var zombies = Assert.Single(document.Entries, item => item.Path == "Zombies");
        var multiplier = Assert.Single(document.Entries, item => item.Path == "MultiplierConfig.Global");

        Assert.Equal(SandboxValueType.Integer, zombies.ValueType);
        Assert.Equal("Insane", zombies.EnumOptions[0].Label);
        Assert.Equal("4", zombies.DefaultValue);
        Assert.Equal(SandboxValueType.Double, multiplier.ValueType);
        Assert.Equal(0m, multiplier.Minimum);
        Assert.Equal(1000m, multiplier.Maximum);
        Assert.Equal("1.00", multiplier.DefaultValue);
    }

    [Fact]
    public void Set_ValidatesCurrentTypeLimitsAndEnum()
    {
        var document = SandboxVarsDocument.Parse(ReadSandbox());

        Assert.Throws<ArgumentException>(() => document.Set("ZombieVoronoiNoise", "1"));
        Assert.Throws<ArgumentException>(() => document.Set("MultiplierConfig.Global", "1000.01"));
        Assert.Throws<ArgumentException>(() => document.Set("Zombies", "7"));
        document.Set("LootItemRemovalList", "Base.Axe");
        Assert.Equal("Base.Axe", document.Get("LootItemRemovalList"));
        Assert.Equal("\"Base.Axe\"", document.GetRaw("LootItemRemovalList"));
    }

    [Fact]
    public void Parse_RejectsUnexpectedLuaWithClearLineNumber()
    {
        var error = Assert.Throws<FormatException>(() => SandboxVarsDocument.Parse(
            "SandboxVars = {\n    A = os.execute('x'),\n}"));

        Assert.Contains("linha 2", error.Message);
        Assert.Contains("sintaxe inesperada", error.Message);
    }

    private static string ReadSandbox() => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "sandbox", "servertest_SandboxVars.lua"));
}
