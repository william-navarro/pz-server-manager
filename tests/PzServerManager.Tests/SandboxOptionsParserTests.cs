using PzServerManager.Application.Configuration;
using PzServerManager.Application.Mods;

namespace PzServerManager.Tests;

public sealed class SandboxOptionsParserTests
{
    [Fact]
    public void Parse_ReadsEqualsStyleAndLinksSyntheticValue()
    {
        var definitions = SandboxOptionsParser.Parse(File.ReadAllText(ALifeOptions));
        var option = Assert.Single(definitions, item => item.Path == "ExampleNpc.EncounterChance");
        var sandbox = SandboxVarsDocument.Parse(File.ReadAllText(SandboxFixture));

        Assert.Equal(2, definitions.Count);
        Assert.Equal(SandboxValueType.Integer, option.ValueType);
        Assert.Equal(0m, option.Minimum);
        Assert.Equal(100m, option.Maximum);
        Assert.Equal("25", option.DefaultValue);
        Assert.Equal("40", sandbox.Get(option.Path));
    }

    [Fact]
    public void Parse_ReadsLegacyBraceStyleAndDouble()
    {
        var definitions = SandboxOptionsParser.Parse(File.ReadAllText(MoreTraitsOptions));
        var option = Assert.Single(definitions, item => item.Path == "ExampleTraits.Distance");

        Assert.Equal(2, definitions.Count);
        Assert.Equal(SandboxValueType.Double, option.ValueType);
        Assert.Equal(.25m, option.Minimum);
        Assert.Equal(5m, option.Maximum);
        Assert.Equal("1.75", option.DefaultValue);
    }

    [Fact]
    public void Parse_IgnoresCommentsTrailingCommasAndUnknownProperties()
    {
        const string text = """
            VERSION = 1,
            option Demo.Mode = { -- comentário
                type = enum, numValues = 2, default = 1,
                unknownThing = ignored, page = Demo, translation = Demo_Mode,
            }
            """;

        var option = Assert.Single(SandboxOptionsParser.Parse(text));
        Assert.Equal(2, option.NumberOfValues);
        Assert.Equal("Demo", option.Page);
    }

    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static string SandboxFixture => Path.Combine(Fixtures, "sandbox", "servertest_SandboxVars.lua");
    private static string ALifeOptions => Path.Combine(Fixtures, "workshop", "1000000004", "mods", "Example NPC", "42.20", "media", "sandbox-options.txt");
    private static string MoreTraitsOptions => Path.Combine(Fixtures, "workshop", "1000000001", "mods", "Example Traits", "42.20", "media", "sandbox-options.txt");
}
