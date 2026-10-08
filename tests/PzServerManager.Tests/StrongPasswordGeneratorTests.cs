using PzServerManager.Application.Whitelist;

namespace PzServerManager.Tests;

public sealed class StrongPasswordGeneratorTests
{
    [Fact]
    public void Generate_ReturnsTwelveRandomAlphanumericCharacters()
    {
        var passwords = Enumerable.Range(0, 20).Select(_ => StrongPasswordGenerator.Generate()).ToArray();
        Assert.All(passwords, password =>
        {
            Assert.Equal(12, password.Length);
            Assert.All(password, character => Assert.True(char.IsAsciiLetterOrDigit(character)));
            Assert.Contains(password, char.IsAsciiLetterUpper);
            Assert.Contains(password, char.IsAsciiLetterLower);
            Assert.Contains(password, char.IsAsciiDigit);
        });
        Assert.Equal(passwords.Length, passwords.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Generate_RejectsWeakLength() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => StrongPasswordGenerator.Generate(11));
}
