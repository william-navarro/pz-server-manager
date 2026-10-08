using System.Text.RegularExpressions;

namespace PzServerManager.Application.Rcon;

public static partial class RconArgumentValidator
{
    public const int MaximumMessageLength = 200;

    public static string UserName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (!UserNamePattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "O nome deve ter de 1 a 32 caracteres e usar apenas letras, números, espaço, ponto, sublinhado ou hífen.",
                nameof(value));
        }

        return normalized;
    }

    public static string Text(string value, string argumentName = "texto", int maximumLength = MaximumMessageLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"{argumentName} deve ter de 1 a {maximumLength} caracteres.", argumentName);
        }

        if (normalized.Contains('"') || normalized.Any(char.IsControl))
        {
            throw new ArgumentException($"{argumentName} não pode conter aspas, quebras de linha ou caracteres de controle.", argumentName);
        }

        return normalized;
    }

    public static string Password(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > MaximumMessageLength || value.Contains('"') || value.Any(char.IsControl))
        {
            throw new ArgumentException("A senha deve ter de 1 a 200 caracteres e não pode conter aspas ou caracteres de controle.", nameof(value));
        }

        return value;
    }

    [GeneratedRegex("^[A-Za-z0-9_.\\- ]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserNamePattern();
}
