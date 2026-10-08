using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PzServerManager.Application.Abstractions;
using PzServerManager.Application.ConfigurationHistory;

namespace PzServerManager.Infrastructure.Configuration;

public sealed class ConfigurationHistoryService(
    IOptions<ManagerOptions> options,
    IAuditLog auditLog,
    IServerOperationLock operationLock) : IConfigurationHistoryService
{
    private readonly ManagedServerOptions _options = options.Value.ManagedServer
        ?? throw new InvalidOperationException("O servidor Linux gerenciado não foi configurado.");

    public Task<IReadOnlyList<ConfigurationBackup>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = Candidates().OrderByDescending(item => item.LastModified).ToArray();
        return Task.FromResult<IReadOnlyList<ConfigurationBackup>>(files);
    }

    public async Task<ConfigurationComparison> CompareAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var backup = Resolve(fileName);
        var currentPath = CurrentPath(backup.Kind);
        var oldText = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(currentPath)!, backup.FileName), cancellationToken);
        var currentText = await File.ReadAllTextAsync(currentPath, cancellationToken);
        return new(backup, Path.GetFileName(currentPath), LineDiff.Compare(oldText, currentText));
    }

    public async Task RestoreAsync(string actor, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var backup = Resolve(fileName);
        var currentPath = CurrentPath(backup.Kind);
        using var lease = operationLock.TryAcquire("restaurar configuração");
        if (lease is null) throw new InvalidOperationException($"Outra operação já está em andamento: {operationLock.CurrentOperation}.");
        var sourcePath = Path.Combine(Path.GetDirectoryName(currentPath)!, backup.FileName);
        var text = await File.ReadAllTextAsync(sourcePath, AtomicTextFileStore.Utf8WithoutBom, cancellationToken);
        await AtomicTextFileStore.WriteAsync(currentPath, text, cancellationToken);
        await auditLog.WriteAsync(actor, "restaurar configuração", backup.FileName, CancellationToken.None);
    }

    private ConfigurationBackup Resolve(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
            throw new InvalidOperationException("O arquivo de configuração escolhido é inválido.");
        return Candidates().SingleOrDefault(item => item.FileName.Equals(fileName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("O arquivo escolhido não existe mais ou não é um backup permitido.");
    }

    private IEnumerable<ConfigurationBackup> Candidates()
    {
        var directory = Path.GetDirectoryName(_options.ServerIniPath)!;
        if (!Directory.Exists(directory)) yield break;
        foreach (var kind in Enum.GetValues<ConfigurationFileKind>())
        {
            var current = CurrentPath(kind);
            var baseName = Path.GetFileName(current);
            var pattern = new Regex($"^{Regex.Escape(baseName)}\\.(?:[0-9]{{8}}-[0-9]{{6}}-[0-9]{{3}}\\.bak|antes-[A-Za-z0-9._-]+|original)$", RegexOptions.CultureInvariant);
            foreach (var path in Directory.EnumerateFiles(directory, baseName + ".*", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(path);
                if (pattern.IsMatch(info.Name)) yield return new(info.Name, kind, info.LastWriteTimeUtc, info.Length);
            }
        }
    }

    private string CurrentPath(ConfigurationFileKind kind) => kind == ConfigurationFileKind.ServerIni
        ? _options.ServerIniPath : _options.SandboxVarsPath;
}
