using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

internal sealed class ServiceHealthMonitor : IServiceHealthMonitor
{
    private readonly IEnumerable<ITorrentDownloadService> _torrentServices;
    private readonly IEnumerable<IHostedDownloadService> _hostedServices;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ServiceHealthStatus> _healthStatus;

    public ServiceHealthMonitor(
        IEnumerable<ITorrentDownloadService> torrentServices,
        IEnumerable<IHostedDownloadService> hostedServices,
        ILogger<ServiceHealthMonitor> logger)
    {
        _torrentServices = torrentServices;
        _hostedServices = hostedServices;
        _logger = logger;
        _healthStatus = new ConcurrentDictionary<string, ServiceHealthStatus>();
    }

    public async Task CheckAllServicesAsync(CancellationToken ct)
    {
        var config = TeleJellyPlugin.Instance?.Configuration.DownloadManager.HealthMonitoring;
        if (config?.Enabled == false)
        {
            return;
        }

        _logger.LogDebug("Starting health checks for all download services");

        var checkTasks = new List<Task>();

        foreach (var service in _torrentServices)
        {
            checkTasks.Add(CheckServiceHealthAsync(service.ServiceName, service.IsEnabled,
                async () => await service.TestConnectionAsync(ct), ct));
        }

        foreach (var service in _hostedServices)
        {
            checkTasks.Add(CheckServiceHealthAsync(service.ServiceName, service.IsEnabled,
                async () => await service.TestConnectionAsync(ct), ct));
        }

        await Task.WhenAll(checkTasks);

        _logger.LogDebug("Completed health checks for all download services");
    }

    public ServiceHealthStatus? GetServiceHealth(string serviceName)
    {
        return _healthStatus.TryGetValue(serviceName, out var status) ? status : null;
    }

    public IEnumerable<ITorrentDownloadService> GetAvailableTorrentServices()
    {
        return _torrentServices
            .Where(s => s.IsEnabled && IsServiceHealthy(s.ServiceName))
            .OrderBy(s => GetServicePriority(s.ServiceName));
    }

    public IEnumerable<IHostedDownloadService> GetAvailableHostedServices()
    {
        return _hostedServices
            .Where(s => s.IsEnabled && IsServiceHealthy(s.ServiceName))
            .OrderBy(s => GetServicePriority(s.ServiceName));
    }

    private async Task CheckServiceHealthAsync(
        string serviceName,
        bool isEnabled,
        Func<Task<bool>> testConnection,
        CancellationToken ct)
    {
        if (!isEnabled)
        {
            _healthStatus.TryRemove(serviceName, out _);
            return;
        }

        var now = DateTime.UtcNow;
        var status = _healthStatus.GetOrAdd(serviceName, _ => new ServiceHealthStatus
        {
            ServiceName = serviceName,
            State = HealthState.Offline,
            LastCheck = now
        });

        try
        {
            var success = await testConnection();

            status.LastCheck = now;

            if (success)
            {
                status.ConsecutiveFailures = 0;
                status.LastSuccess = now;
                status.State = HealthState.Online;
                status.LastError = null;
                _logger.LogDebug("Service {ServiceName} is healthy", serviceName);
            }
            else
            {
                UpdateFailedStatus(status, "Connection test returned false");
            }
        }
        catch (Exception ex)
        {
            status.LastCheck = now;
            UpdateFailedStatus(status, ex.Message);
        }
    }

    private void UpdateFailedStatus(ServiceHealthStatus status, string error)
    {
        status.ConsecutiveFailures++;
        status.LastError = error;

        var config = TeleJellyPlugin.Instance?.Configuration.DownloadManager.HealthMonitoring;
        var maxFailures = config?.MaxConsecutiveFailures ?? 3;

        if (status.ConsecutiveFailures >= maxFailures)
        {
            status.State = HealthState.Offline;
            _logger.LogWarning(
                "Service {ServiceName} is OFFLINE after {Failures} consecutive failures: {Error}",
                status.ServiceName,
                status.ConsecutiveFailures,
                error
            );
        }
        else if (status.ConsecutiveFailures > 0)
        {
            status.State = HealthState.Degraded;
            _logger.LogWarning(
                "Service {ServiceName} is DEGRADED ({Failures} consecutive failures): {Error}",
                status.ServiceName,
                status.ConsecutiveFailures,
                error
            );
        }
    }

    private bool IsServiceHealthy(string serviceName)
    {
        if (!_healthStatus.TryGetValue(serviceName, out var status))
        {
            return true;
        }

        return status.State == HealthState.Online || status.State == HealthState.Degraded;
    }

    private int GetServicePriority(string serviceName)
    {
        return serviceName switch
        {
            "Transmission" => 1,
            "qBittorrent" => 2,
            "JDownloader2" => 1,
            "pyLoad" => 2,
            _ => 99
        };
    }
}
