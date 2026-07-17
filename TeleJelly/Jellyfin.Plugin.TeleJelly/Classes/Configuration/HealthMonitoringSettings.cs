namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration;

public class HealthMonitoringSettings
{
    public bool Enabled { get; set; } = true;
    public int CheckIntervalMinutes { get; set; } = 5;
    public int MaxConsecutiveFailures { get; set; } = 3;
}