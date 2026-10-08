namespace PzServerManager.Application.Automation;

public enum UpdateMode
{
    Automatico,
    SoAvisar
}

public sealed record AutomationSettings
{
    public int[] WarningSeconds { get; set; } = [300, 60, 10];
    public bool DailyRestartEnabled { get; set; }
    public string DailyRestartTime { get; set; } = "05:30";
    public int CheckIntervalMinutes { get; set; } = 15;
    public UpdateMode UpdateMode { get; set; } = UpdateMode.Automatico;

    public AutomationSettings Validate()
    {
        if (WarningSeconds is null || WarningSeconds.Length == 0 ||
            WarningSeconds.Any(value => value < 5 || value > 3600) ||
            WarningSeconds.Distinct().Count() != WarningSeconds.Length)
            throw new ArgumentException("Os avisos devem ser segundos distintos entre 5 e 3600.");
        if (CheckIntervalMinutes is < 5 or > 1440)
            throw new ArgumentException("O intervalo de verificação deve ficar entre 5 e 1440 minutos.");
        if (!TimeOnly.TryParseExact(DailyRestartTime, "HH:mm", out _))
            throw new ArgumentException("O horário diário deve usar o formato HH:mm.");
        return this with { WarningSeconds = WarningSeconds.OrderByDescending(value => value).ToArray() };
    }
}
