namespace PzServerManager.Application.Backups;

public enum BackupReason
{
    Manual,
    Daily,
    BeforeRestore,
    BeforeReset,
    UpdateMods,
    UpdateGame
}

public sealed record BackupFile(
    string FileName,
    DateTimeOffset Timestamp,
    BackupReason Reason,
    long SizeBytes)
{
    public string ReasonLabel => Reason switch
    {
        BackupReason.Manual => "Manual",
        BackupReason.Daily => "Diário",
        BackupReason.BeforeRestore => "Antes de restaurar",
        BackupReason.BeforeReset => "Antes de zerar",
        BackupReason.UpdateMods => "Atualização de mods",
        BackupReason.UpdateGame => "Atualização do jogo",
        _ => "Desconhecido"
    };
}

public sealed record BackupServiceResult(string UnitName, string Result, DateTimeOffset? FinishedAt);

public sealed record BackupOverview(
    IReadOnlyList<BackupFile> Files,
    DateTimeOffset? NextDailyBackup,
    IReadOnlyList<BackupServiceResult> LastResults);

public sealed record BackupOperationResult(
    bool Succeeded,
    bool TimedOut,
    string Message,
    string Journal);
