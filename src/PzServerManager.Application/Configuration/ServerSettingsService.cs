using PzServerManager.Application.Abstractions;

namespace PzServerManager.Application.Configuration;

public sealed class ServerSettingsService(IServerIniStore store, IAuditLog auditLog)
{
    public async Task<ServerSettingsSnapshot> LoadAsync(string path, CancellationToken ct = default)
    {
        var document = await store.LoadAsync(path, ct);
        return new(document.ToText(), ServerSettingsCatalog.Create(document));
    }

    public async Task<IReadOnlyList<ServerSettingChange>> SaveAsync(
        string path,
        string originalText,
        IReadOnlyDictionary<string, string> values,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(values);
        var current = await store.LoadAsync(path, ct);
        if (!string.Equals(current.ToText(), originalText, StringComparison.Ordinal))
        {
            throw new ServerSettingsConflictException("O arquivo foi alterado no disco. Recarregue a página antes de salvar.");
        }

        var settings = ServerSettingsCatalog.Create(current).ToDictionary(item => item.Key, StringComparer.Ordinal);
        var changes = new List<ServerSettingChange>();
        foreach (var (key, value) in values)
        {
            if (!settings.TryGetValue(key, out var setting)) throw new ArgumentException($"A configuração '{key}' não existe.", nameof(values));
            if (string.Equals(setting.Value, value, StringComparison.Ordinal)) continue;
            if (setting.IsReadOnly) throw new InvalidOperationException($"A configuração '{key}' é somente leitura: {setting.ReadOnlyReason}");
            ServerSettingsCatalog.ValidateValue(setting, value);
            current.Set(key, value);
            changes.Add(new(key, setting.Value, value, setting.IsSensitive));
        }

        if (changes.Count == 0) return changes;
        await store.SaveAsync(path, current, ct);
        foreach (var change in changes)
        {
            var detail = change.IsSensitive ? $"{change.Key}: valor sensível alterado" : $"{change.Key}: {change.Before} -> {change.After}";
            await auditLog.WriteAsync(actor, "alterar configuração", detail, CancellationToken.None);
        }
        return changes;
    }

    public Task AuditSensitiveRevealAsync(string key, string actor, CancellationToken ct = default)
    {
        if (!ServerIniDocument.IsSensitive(key)) throw new ArgumentException("A chave não é sensível.", nameof(key));
        return auditLog.WriteAsync(actor, "revelar configuração sensível", key, ct);
    }
}

public sealed class ServerSettingsConflictException(string message) : InvalidOperationException(message);
