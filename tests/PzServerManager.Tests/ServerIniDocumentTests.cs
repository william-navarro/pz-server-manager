using PzServerManager.Application.Configuration;

namespace PzServerManager.Tests;

public sealed class ServerIniDocumentTests
{
    [Fact]
    public void Parse_RoundTripsFixtureExactly()
    {
        var text = ReadFixture();

        var document = ServerIniDocument.Parse(text);

        Assert.Equal(text, document.ToText());
        Assert.False(document.HasChanges);
    }

    [Fact]
    public void Get_ReadsScalarAndEmptyValuesFromFixture()
    {
        var document = ServerIniDocument.Parse(ReadFixture());

        Assert.Equal("Muldraugh, KY", document.Get("Map"));
        Assert.Equal(["Muldraugh, KY"], document.GetList("Map"));
        Assert.Equal(string.Empty, document.Get("Password"));
        Assert.Null(document.Get("Missing"));
    }

    [Fact]
    public void Set_ExistingKeyChangesOnlyItsLine()
    {
        var originalLines = ReadFixture().Split('\n');
        var document = ServerIniDocument.Parse(ReadFixture());

        document.Set("PVP", "false");

        var changedLines = document.ToText().Split('\n');
        Assert.Equal(originalLines.Length, changedLines.Length);
        Assert.Equal(1, originalLines.Zip(changedLines).Count(pair => pair.First != pair.Second));
        Assert.Equal("PVP=false", changedLines[1]);
        Assert.True(document.HasChanges);
    }

    [Fact]
    public void Set_NewKeyAppendsAtEndWithoutChangingFinalNewLineBehavior()
    {
        var original = ReadFixture();
        var document = ServerIniDocument.Parse(original);

        document.Set("NovaChave", "novo valor");

        Assert.Equal($"{original}\nNovaChave=novo valor", document.ToText());
        Assert.False(document.ToText().EndsWith('\n'));
    }

    [Fact]
    public void GetDescription_ReturnsImmediatelyPrecedingComments()
    {
        var document = ServerIniDocument.Parse(ReadFixture());

        Assert.Equal("Players can hurt and kill other players", document.GetDescription("PVP"));
        Assert.Null(ServerIniDocument.Parse("# distante\n\nChave=valor").GetDescription("Chave"));
    }

    [Fact]
    public void Parse_ValueMayContainEqualsSign()
    {
        var document = ServerIniDocument.Parse("A=b=c");

        Assert.Equal("b=c", document.Get("A"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A=B")]
    [InlineData("#A")]
    [InlineData("A B")]
    [InlineData("A\tB")]
    [InlineData("A\nB")]
    public void Set_RejectsInvalidKey(string key)
    {
        var document = ServerIniDocument.Parse(string.Empty);

        Assert.Throws<ArgumentException>(() => document.Set(key, "valor"));
    }

    [Theory]
    [InlineData("uma\nduas")]
    [InlineData("uma\rduas")]
    [InlineData("uma\r\nduas")]
    public void Set_RejectsValueWithLineBreak(string value)
    {
        var document = ServerIniDocument.Parse(string.Empty);

        Assert.Throws<ArgumentException>(() => document.Set("Chave", value));
    }

    [Fact]
    public void SetList_AndGetListNormalizeListItems()
    {
        var document = ServerIniDocument.Parse("Mods= A ; ;B;; C ");

        Assert.Equal(["A", "B", "C"], document.GetList("Mods"));

        document.SetList("Mods", [" ModA ", "ModB"]);

        Assert.Equal("ModA;ModB", document.Get("Mods"));
        Assert.Equal(["ModA", "ModB"], document.GetList("Mods"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A;B")]
    public void SetList_RejectsInvalidItem(string item)
    {
        var document = ServerIniDocument.Parse(string.Empty);

        Assert.Throws<ArgumentException>(() => document.SetList("Mods", [item]));
    }

    [Theory]
    [InlineData("Password", true)]
    [InlineData("RCONPassword", true)]
    [InlineData("discordtoken", true)]
    [InlineData("ApiTokenValue", true)]
    [InlineData("Username", false)]
    public void IsSensitive_DetectsPasswordAndToken(string key, bool expected)
    {
        Assert.Equal(expected, ServerIniDocument.IsSensitive(key));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Set_PreservesOriginalLineBreak(string newLine)
    {
        var document = ServerIniDocument.Parse($"A=1{newLine}B=2{newLine}");

        document.Set("B", "3");
        document.Set("C", "4");

        Assert.Equal($"A=1{newLine}B=3{newLine}C=4{newLine}", document.ToText());
    }

    [Fact]
    public void DuplicateKeys_ReturnsEachDuplicateOnceInFileOrder()
    {
        var document = ServerIniDocument.Parse("A=1\nB=2\nA=3\nA=4\nC=5\nB=6");

        Assert.Equal(["A", "B", "C"], document.Keys);
        Assert.Equal(["A", "B"], document.DuplicateKeys);
        Assert.Equal("4", document.Get("A"));

        document.Set("A", "5");

        Assert.Equal("A=1\nB=2\nA=3\nA=5\nC=5\nB=6", document.ToText());
    }

    [Fact]
    public void Keys_AreCaseSensitive()
    {
        var document = ServerIniDocument.Parse("Key=one\nkey=two");

        Assert.True(document.ContainsKey("Key"));
        Assert.True(document.ContainsKey("key"));
        Assert.False(document.ContainsKey("KEY"));
        Assert.Equal(["Key", "key"], document.Keys);
    }

    private static string ReadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "servertest.ini"));
}
