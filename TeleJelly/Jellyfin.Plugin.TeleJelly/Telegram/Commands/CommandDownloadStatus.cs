using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Jellyfin.Plugin.TeleJelly.Services.Download.Health;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

public class CommandDownloadStatus : ICommandBase
{
    private readonly IServiceHealthMonitor _healthMonitor;
    private readonly IDownloadOrchestrator _orchestrator;

    public CommandDownloadStatus(IDownloadOrchestrator orchestrator, IServiceHealthMonitor healthMonitor)
    {
        _orchestrator = orchestrator;
        _healthMonitor = healthMonitor;
    }

    public string Command => "download_status";
    public bool NeedsAdmin => false;

    public async Task Execute(ITelegramBotService botService, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var client = botService.BotClientWrapper.Client;
        if (client == null)
        {
            botService.Logger.LogError("Telegram Bot Client wrapper is null in CommandLink.");
            return;
        }

        var downloads = _orchestrator.GetAllDownloads()
            .Where(d => d.ChatId == message.Chat.Id)
            .OrderByDescending(d => d.StartedAt)
            .Take(10).ToArray(); // Limit to 10 most recent downloads

        if (!downloads.Any())
        {
            await client.SendMessage(
                message.Chat.Id,
                "No active or recent downloads.",
                cancellationToken: cancellationToken);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("<b>Your Recent Downloads:</b>");
        sb.AppendLine();

        foreach (var download in downloads)
        {
            var etaString = "";
            if (download.Status == DownloadStatus.Downloading && download.ProgressPercentage > 0)
            {
                var remainingMinutes = (100 - download.ProgressPercentage) / download.ProgressPercentage * (DateTime.UtcNow - download.StartedAt).TotalMinutes;
                var eta = TimeSpan.FromMinutes(remainingMinutes);
                etaString = $" (ETA: {eta:hh\\:mm})";
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"<b>{download.Title} ({download.Year})</b>");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Status: <i>{download.Status}</i> {download.ProgressPercentage:F1}%{etaString}");
            if (download.RequiresExtraction)
            {
                var attemptedPasswords = download.TriedPasswords?.Length ?? 0;
                sb.AppendLine(CultureInfo.InvariantCulture, $"Extraction: required{(attemptedPasswords > 0 ? $" | password candidates: {attemptedPasswords}" : string.Empty)}");
            }

            if (!string.IsNullOrEmpty(download.ErrorMessage))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"Error: <pre>{download.ErrorMessage}</pre>");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"Started: {download.StartedAt:g}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"ID: <code>{download.Id}</code>");
            sb.AppendLine("---");
        }

        var health = _healthMonitor.GetAllServiceHealth().ToArray();
        if (health.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("<b>Service Health:</b>");
            foreach (var status in health)
            {
                var lastSuccess = status.LastSuccess?.ToString("g", CultureInfo.InvariantCulture) ?? "never";
                sb.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"• <b>{status.ServiceName}</b>: <i>{status.State}</i> | Failures: {status.ConsecutiveFailures} | Last success: {lastSuccess}");
            }
        }

        await client.SendMessage(
            message.Chat.Id,
            sb.ToString(),
            ParseMode.Html,
            cancellationToken: cancellationToken);
    }
}
