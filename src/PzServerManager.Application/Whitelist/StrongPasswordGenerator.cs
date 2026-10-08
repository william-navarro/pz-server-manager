using System.Security.Cryptography;

namespace PzServerManager.Application.Whitelist;

public static class StrongPasswordGenerator
{
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Characters = Uppercase + Lowercase + Digits;

    public static string Generate(int length = 12)
    {
        if (length < 12) throw new ArgumentOutOfRangeException(nameof(length), "A senha deve ter pelo menos 12 caracteres.");
        Span<char> result = length <= 256 ? stackalloc char[length] : new char[length];
        result[0] = RandomCharacter(Uppercase);
        result[1] = RandomCharacter(Lowercase);
        result[2] = RandomCharacter(Digits);
        for (var index = 3; index < result.Length; index++)
        {
            result[index] = RandomCharacter(Characters);
        }
        for (var index = result.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (result[index], result[swapIndex]) = (result[swapIndex], result[index]);
        }

        return new string(result);
    }

    private static char RandomCharacter(string characters) => characters[RandomNumberGenerator.GetInt32(characters.Length)];
}
