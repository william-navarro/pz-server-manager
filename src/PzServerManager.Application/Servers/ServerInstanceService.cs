using System.Globalization;
using System.Text;
using PzServerManager.Application.Abstractions;
using PzServerManager.Domain.Servers;

namespace PzServerManager.Application.Servers;

public sealed class ServerInstanceService(
    IServerInstanceRepository repository,
    IManagerPaths paths)
{
    public async Task<IReadOnlyList<ServerInstanceDto>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var instances = await repository.ListAsync(cancellationToken);
        return instances.Select(ToDto).ToArray();
    }

    public async Task<ServerInstanceDto> CreateAsync(
        CreateServerInstanceCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            throw new ServerInstanceValidationException("Informe um nome para o servidor.");
        }

        var slug = CreateSlug(command.Name);
        var existing = await repository.ListAsync(cancellationToken);

        if (existing.Any(item => item.Name.Equals(command.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new ServerInstanceValidationException("Já existe uma instância com esse nome.");
        }

        if (existing.Any(item => item.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ServerInstanceValidationException("O nome gera um identificador já utilizado por outra instância.");
        }

        if (existing.Any(item => item.GamePort == command.GamePort || item.RconPort == command.GamePort))
        {
            throw new ServerInstanceValidationException($"A porta {command.GamePort} já está reservada por outra instância.");
        }

        if (existing.Any(item => item.GamePort == command.RconPort || item.RconPort == command.RconPort))
        {
            throw new ServerInstanceValidationException($"A porta {command.RconPort} já está reservada por outra instância.");
        }

        ServerInstance instance;
        try
        {
            var instanceRoot = Path.Combine(paths.InstancesRoot, slug);
            instance = ServerInstance.Create(
                command.Name,
                slug,
                Path.Combine(instanceRoot, "server"),
                Path.Combine(instanceRoot, "data"),
                command.Branch,
                command.GamePort,
                command.RconPort);
        }
        catch (ArgumentException exception)
        {
            throw new ServerInstanceValidationException(exception.Message);
        }

        await repository.AddAsync(instance, cancellationToken);
        return ToDto(instance);
    }

    internal static string CreateSlug(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasSeparator = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ServerInstanceValidationException("O nome precisa conter pelo menos uma letra ou número.");
        }

        return slug;
    }

    private static ServerInstanceDto ToDto(ServerInstance instance) => new(
        instance.Id,
        instance.Name,
        instance.Slug,
        instance.InstallationPath,
        instance.DataPath,
        instance.Branch,
        instance.GamePort,
        instance.RconPort,
        instance.Status,
        instance.CreatedAtUtc);
}
