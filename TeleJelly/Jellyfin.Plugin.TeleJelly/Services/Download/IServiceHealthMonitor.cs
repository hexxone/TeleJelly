using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

public interface IServiceHealthMonitor
{
    Task CheckAllServicesAsync(CancellationToken ct);
    // TODO unused ??
    ServiceHealthStatus? GetServiceHealth(string serviceName);
    IEnumerable<ITorrentDownloadService> GetAvailableTorrentServices();
    IEnumerable<IHostedDownloadService> GetAvailableHostedServices();
}

public sealed class ServiceHealthStatus
{
    public string ServiceName { get; set; } = string.Empty;
    public HealthState State { get; set; }
    public DateTime LastCheck { get; set; }
    public DateTime? LastSuccess { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }
}

public enum HealthState
{
    Online,
    Degraded,
    Offline
}
