using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

internal sealed class DownloadManagerService : BackgroundService
{
    private readonly IDownloadOrchestrator _orchestrator;
    private readonly IServiceHealthMonitor _healthMonitor;
    private readonly PeriodicTimer _downloadTimer = new(TimeSpan.FromSeconds(10));

    public DownloadManagerService(IDownloadOrchestrator orchestrator, IServiceHealthMonitor healthMonitor)
    {
        _orchestrator = orchestrator;
        _healthMonitor = healthMonitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
