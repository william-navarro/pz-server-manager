namespace PzServerManager.Application.ManagedServer;

public static class ServerConsoleCommand
{
    public const int MaximumLength = 300;

    public static string Validate(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        if (command.Contains('\r') || command.Contains('\n'))
        {
            throw new ArgumentException("O comando não pode conter quebra de linha.", nameof(command));
        }

        var normalized = command.Trim();
        if (normalized.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"O comando deve possuir no máximo {MaximumLength} caracteres.",
                nameof(command));
        }

        return normalized;
    }
}
