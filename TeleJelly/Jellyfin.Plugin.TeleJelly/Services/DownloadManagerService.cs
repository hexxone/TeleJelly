using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.TeleJelly.Services
{
    public class DownloadManagerService : BackgroundService
    {
        private readonly DownloadOrchestrator _orchestrator;
        private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(10));

        public DownloadManagerService(DownloadOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _orchestrator.RestoreDownloadsAsync(stoppingToken);

            while (await _timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
            {
                await _orchestrator.ProcessAllDownloadsAsync(stoppingToken);
            }
        }
    }
}
