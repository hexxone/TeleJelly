using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;
using Jellyfin.Plugin.TeleJelly.Services.Download.Torrents;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Health;

internal sealed class ServiceHealthMonitor : IServiceHealthMonitor
{
    private readonly IEnumerable<ITorrentDownloadService> _torrentServices;
    private readonly IEnumerable<IHostedDownloadService> _hostedServices;
    private readonly TelegramBotClientWrapper _botClientWrapper;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ServiceHealthStatus> _healthStatus;
    private readonly ConcurrentDictionary<string, byte> _offlineNotificationsSent;

    public ServiceHealthMonitor(
        IEnumerable<ITorrentDownloadService> torrentServices,
        IEnumerable<IHostedDownloadService> hostedServices,
        TelegramBotClientWrapper botClientWrapper,
        ILogger<ServiceHealthMonitor> logger)
    {
        _torrentServices = torrentServices;
        _hostedServices = hostedServices;
        _botClientWrapper = botClientWrapper;
        _logger = logger;
        _healthStatus = new ConcurrentDictionary<string, ServiceHealthStatus>();
        _offlineNotificationsSent = new ConcurrentDictionary<string, byte>();
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

    public IEnumerable<ServiceHealthStatus> GetAllServiceHealth()
    {
        return _healthStatus.Values
            .OrderBy(s => GetServicePriority(s.ServiceName))
            .ThenBy(s => s.ServiceName);
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
            _offlineNotificationsSent.TryRemove(serviceName, out _);
            return;
        }

        var now = DateTime.UtcNow;
        var status = _healthStatus.GetOrAdd(serviceName, _ => new ServiceHealthStatus
        {
            ServiceName = serviceName,
            State = HealthState.Offline,
            LastCheck = now
        });
        var previousState = status.State;

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
                _offlineNotificationsSent.TryRemove(serviceName, out _);
                if (previousState != HealthState.Online)
                {
                    _logger.LogInformation("Service {ServiceName} is ONLINE again", serviceName);
                }
            }
            else
            {
                await UpdateFailedStatusAsync(status, previousState, "Connection test returned false", ct);
            }
        }
        catch (Exception ex)
        {
            status.LastCheck = now;
            await UpdateFailedStatusAsync(status, previousState, ex.Message, ct);
        }
    }

    private async Task UpdateFailedStatusAsync(ServiceHealthStatus status, HealthState previousState, string error, CancellationToken ct)
    {
        status.ConsecutiveFailures++;
        status.LastError = error;

        var config = TeleJellyPlugin.Instance?.Configuration.DownloadManager.HealthMonitoring;
        var maxFailures = config?.MaxConsecutiveFailures ?? 3;

        if (status.ConsecutiveFailures >= maxFailures)
        {
            status.State = HealthState.Offline;
            if (previousState != HealthState.Offline)
            {
                _logger.LogWarning(
                    "Service {ServiceName} is OFFLINE after {Failures} consecutive failures: {Error}",
                    status.ServiceName,
                    status.ConsecutiveFailures,
                    error
                );
            }

            await NotifyAdminsServiceOfflineAsync(status, ct);
        }
        else if (status.ConsecutiveFailures > 0)
        {
            status.State = HealthState.Degraded;
            if (previousState != HealthState.Degraded)
            {
                _logger.LogWarning(
                    "Service {ServiceName} is DEGRADED ({Failures} consecutive failures): {Error}",
                    status.ServiceName,
                    status.ConsecutiveFailures,
                    error
                );
            }
        }
    }

    private async Task NotifyAdminsServiceOfflineAsync(ServiceHealthStatus status, CancellationToken ct)
    {
        var config = TeleJellyPlugin.Instance?.Configuration;
        var client = _botClientWrapper.Client;
        if (config == null || client == null)
        {
            return;
        }

        var chatIds = config.TelegramGroups
            .Select(group => group.TelegramGroupChat?.TelegramChatId)
            .Where(chatId => chatId.HasValue && chatId.Value != 0)
            .Select(chatId => chatId!.Value)
            .Distinct()
            .ToArray();

        if (chatIds.Length == 0)
        {
            return;
        }

        if (!_offlineNotificationsSent.TryAdd(status.ServiceName, 0))
        {
            return;
        }

        var adminMentions = config.AdminUserNames.Count > 0
            ? "\n" + string.Join(" ", config.AdminUserNames.Select(admin => $"@{admin}"))
            : string.Empty;

        var message = $"Download service '{status.ServiceName}' is offline after {status.ConsecutiveFailures} failed health checks.\nLast error: {status.LastError ?? "unknown"}{adminMentions}";

        try
        {
            foreach (var chatId in chatIds)
            {
                await client.SendMessage(chatId, message, cancellationToken: ct);
            }
        }
        catch
        {
            _offlineNotificationsSent.TryRemove(status.ServiceName, out _);
            throw;
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
