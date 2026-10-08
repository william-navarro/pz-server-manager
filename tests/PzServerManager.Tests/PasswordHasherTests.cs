using PzServerManager.Infrastructure.Authentication;

namespace PzServerManager.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void HashAndVerify_AcceptsCorrectPasswordAndRejectsWrongPassword()
    {
        var hash = PasswordHasher.Hash("uma senha forte");

        Assert.StartsWith($"pbkdf2-sha256${PasswordHasher.IterationCount}$", hash);
        Assert.True(PasswordHasher.Verify("uma senha forte", hash));
        Assert.False(PasswordHasher.Verify("outra senha", hash));
    }

    [Fact]
    public void Hash_UsesRandomSalt() =>
        Assert.NotEqual(PasswordHasher.Hash("mesma senha"), PasswordHasher.Hash("mesma senha"));

    [Fact]
    public void Verify_RejectsMalformedAndWeakHashes()
    {
        Assert.False(PasswordHasher.Verify("senha", "inválido"));
        Assert.False(PasswordHasher.Verify("senha", "pbkdf2-sha256$1$AA==$AA=="));
    }
}
