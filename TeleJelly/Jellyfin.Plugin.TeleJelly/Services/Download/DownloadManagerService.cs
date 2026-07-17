using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Services.Download.Health;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

internal sealed class DownloadManagerService : BackgroundService
{
    private readonly IDownloadOrchestrator _orchestrator;
    private readonly IServiceHealthMonitor _healthMonitor;
    private readonly ILogger<DownloadManagerService> _logger;
    private readonly TelegramBotClientWrapper _botClientWrapper;
    private readonly ConcurrentDictionary<Guid, string> _lastTelegramStatusTexts = new();
    private readonly PeriodicTimer _downloadTimer = new(TimeSpan.FromSeconds(10));
    private DateTime _nextTelegramStatusUpdateAt = DateTime.MinValue;
    private static readonly TimeSpan TelegramStatusUpdateInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan TelegramMessageMaxAge = TimeSpan.FromHours(48);

    public DownloadManagerService(
        IDownloadOrchestrator orchestrator,
        IServiceHealthMonitor healthMonitor,
        TelegramBotClientWrapper botClientWrapper,
        ILogger<DownloadManagerService> logger)
    {
        _orchestrator = orchestrator;
        _healthMonitor = healthMonitor;
        _botClientWrapper = botClientWrapper;
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
            if (DateTime.UtcNow >= _nextTelegramStatusUpdateAt)
            {
                await UpdateTelegramStatusMessagesAsync(stoppingToken);
                _nextTelegramStatusUpdateAt = DateTime.UtcNow + TelegramStatusUpdateInterval;
            }
        }
    }

    private async Task UpdateTelegramStatusMessagesAsync(CancellationToken ct)
    {
        var client = _botClientWrapper.Client;
        if (client == null)
        {
            return;
        }

        var downloads = _orchestrator.GetAllDownloads()
            .Where(download => download.TelegramMessageId.HasValue && download.Status is
                DownloadStatus.Resolving or
                DownloadStatus.Downloading or
                DownloadStatus.Stalled or
                DownloadStatus.Extracting or
                DownloadStatus.ExtractionFailed or
                DownloadStatus.Analyzing or
                DownloadStatus.Organizing or
                DownloadStatus.Completed or
                DownloadStatus.Failed)
            .ToArray();

        foreach (var download in downloads)
        {
            ct.ThrowIfCancellationRequested();
            var text = BuildTelegramStatusText(download);
            if (_lastTelegramStatusTexts.TryGetValue(download.Id, out var previousText) && previousText == text)
            {
                continue;
            }

            var messageIsTooOld = !download.TelegramMessageCreatedAt.HasValue ||
                                  DateTime.UtcNow - download.TelegramMessageCreatedAt.Value > TelegramMessageMaxAge;
            if (!messageIsTooOld)
            {
                try
                {
                    await client.EditMessageText(
                        download.ChatId,
                        download.TelegramMessageId!.Value,
                        text,
                        ParseMode.Html,
                        cancellationToken: ct);
                    await _orchestrator.SetTelegramMessageAsync(download.Id, download.TelegramMessageId.Value, DateTime.UtcNow, ct);
                    _lastTelegramStatusTexts[download.Id] = text;
                    continue;
                }
                catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
                {
                    _lastTelegramStatusTexts[download.Id] = text;
                    continue;
                }
                catch (ApiRequestException ex)
                {
                    _logger.LogWarning(ex, "Could not update Telegram status message {MessageId} for download {DownloadId}; sending a fresh message", download.TelegramMessageId, download.Id);
                }
            }

            try
            {
                var newMessage = await client.SendMessage(download.ChatId, text, ParseMode.Html, cancellationToken: ct);
                await _orchestrator.SetTelegramMessageAsync(download.Id, newMessage.MessageId, newMessage.Date.ToUniversalTime(), ct);
                _lastTelegramStatusTexts[download.Id] = text;
            }
            catch (ApiRequestException ex)
            {
                _logger.LogWarning(ex, "Could not send Telegram status message for download {DownloadId}", download.Id);
            }
        }
    }

    private static string BuildTelegramStatusText(ManagedDownload download)
    {
        var title = WebUtility.HtmlEncode(download.Title);
        var path = WebUtility.HtmlEncode(download.UserConfirmedPath ?? download.SuggestedDestinationPath ?? "not selected");
        var status = WebUtility.HtmlEncode(download.Status.ToString());
        var statusText = WebUtility.HtmlEncode(download.ErrorMessage ?? download.BackendStatusText ?? string.Empty);
        var progressLine = download.Status is DownloadStatus.Resolving or DownloadStatus.Downloading or DownloadStatus.Stalled
            ? $"\n<b>Progress:</b> {download.ProgressPercentage.ToString("F1", CultureInfo.InvariantCulture)}%"
            : string.Empty;
        var detailLine = string.IsNullOrWhiteSpace(statusText) ? string.Empty : $"\n<b>Status text:</b> {statusText}";

        return $"⬇️ <b>{title}</b>\n" +
               $"<b>Status:</b> <i>{status}</i>{progressLine}{detailLine}\n" +
               $"<b>Path:</b> <code>{path}</code>";
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
