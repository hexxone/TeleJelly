using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;
using Jellyfin.Plugin.TeleJelly.Services.Download.Torrents;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Health;

public interface IServiceHealthMonitor
{
    Task CheckAllServicesAsync(CancellationToken ct);
    ServiceHealthStatus? GetServiceHealth(string serviceName);
    IEnumerable<ServiceHealthStatus> GetAllServiceHealth();
    IEnumerable<ITorrentDownloadService> GetAvailableTorrentServices();
    IEnumerable<IHostedDownloadService> GetAvailableHostedServices();
}
