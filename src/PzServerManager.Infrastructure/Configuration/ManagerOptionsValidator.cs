using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace PzServerManager.Infrastructure.Configuration;

public sealed partial class ManagerOptionsValidator : IValidateOptions<ManagerOptions>
{
    public ValidateOptionsResult Validate(string? name, ManagerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DataRoot))
        {
            return ValidateOptionsResult.Fail("PzManager:DataRoot é obrigatório.");
        }

        if (options.ManagedServer is null)
        {
            return ValidateOptionsResult.Success;
        }

        var managed = options.ManagedServer;
        var failures = new List<string>();
        RequireValue(managed.DisplayName, "DisplayName", failures);
        if (string.IsNullOrWhiteSpace(managed.ServerName) || !ServerNamePattern().IsMatch(managed.ServerName))
        {
            failures.Add("PzManager:ManagedServer:ServerName aceita somente A-Z, a-z, 0-9, sublinhado e hífen (1 a 32 caracteres).");
        }
        if (!Version.TryParse(managed.ServerVersion, out _))
        {
            failures.Add("PzManager:ManagedServer:ServerVersion deve ser uma versão válida, por exemplo 42.21.");
        }

        if (string.IsNullOrWhiteSpace(managed.ServiceName) || !ServiceNamePattern().IsMatch(managed.ServiceName))
        {
            failures.Add("PzManager:ManagedServer:ServiceName aceita somente A-Z, a-z, 0-9, @, ponto, sublinhado e hífen.");
        }

        ValidateAbsolutePath(managed.InstallPath, "InstallPath", failures);
        ValidateAbsolutePath(managed.DataPath, "DataPath", failures);
        ValidateAbsolutePath(managed.ConsoleFifo, "ConsoleFifo", failures);
        ValidateAbsolutePath(managed.BackupsPath, "BackupsPath", failures);
        ValidateAbsolutePath(managed.CardinalKeyPath, "CardinalKeyPath", failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireValue(string value, string property, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"PzManager:ManagedServer:{property} é obrigatório.");
        }
    }

    private static void ValidateAbsolutePath(string path, string property, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(path) || (!Path.IsPathFullyQualified(path) && !path.StartsWith('/')))
        {
            failures.Add($"PzManager:ManagedServer:{property} deve ser um caminho absoluto.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9@._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceNamePattern();
    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerNamePattern();
}
