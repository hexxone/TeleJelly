using System;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Health;

public sealed class ServiceHealthStatus
{
    public string ServiceName { get; set; } = string.Empty;
    public HealthState State { get; set; }
    public DateTime LastCheck { get; set; }
    public DateTime? LastSuccess { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }
}