using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services.Download.Health;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

internal sealed class DownloadManagerService : BackgroundService
{
    private readonly IDownloadOrchestrator _orchestrator;
    private readonly IServiceHealthMonitor _healthMonitor;
    private readonly ILogger<DownloadManagerService> _logger;
    private readonly PeriodicTimer _downloadTimer = new(TimeSpan.FromSeconds(10));

    public DownloadManagerService(
        IDownloadOrchestrator orchestrator,
        IServiceHealthMonitor healthMonitor,
        ILogger<DownloadManagerService> logger)
    {
        _orchestrator = orchestrator;
        _healthMonitor = healthMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = TeleJellyPlugin.Instance?.Configuration.DownloadManager;
        if (config?.Enabled != true)
        {
            _logger.LogInformation("Download manager background service is disabled in configuration.");
            return;
        }

        _logger.LogInformation("Starting download manager background service.");
        await _orchestrator.RestoreDownloadsAsync(stoppingToken);

        // Initial health check
        await _healthMonitor.CheckAllServicesAsync(stoppingToken);

        // Start both tasks: download processing and health monitoring
        var downloadTask = ProcessDownloadsAsync(stoppingToken);
        var healthTask = MonitorServiceHealthAsync(stoppingToken);

        await Task.WhenAll(downloadTask, healthTask);
    }

    private async Task ProcessDownloadsAsync(CancellationToken stoppingToken)
    {
        while (await _downloadTimer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
        {
            await _orchestrator.ProcessAllDownloadsAsync(stoppingToken);
        }
    }

    private async Task MonitorServiceHealthAsync(CancellationToken stoppingToken)
    {
        var config = TeleJellyPlugin.Instance?.Configuration.DownloadManager.HealthMonitoring;
        if (config?.Enabled == false)
        {
            _logger.LogInformation("Download manager health monitoring is disabled in configuration.");
            return;
        }

        var checkInterval = TimeSpan.FromMinutes(config?.CheckIntervalMinutes ?? 5);
        var healthTimer = new PeriodicTimer(checkInterval);

        while (await healthTimer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
        {
            await _healthMonitor.CheckAllServicesAsync(stoppingToken);
        }
    }
}
