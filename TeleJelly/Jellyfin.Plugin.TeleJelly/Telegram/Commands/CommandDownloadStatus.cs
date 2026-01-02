using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands
{
    public class CommandDownloadStatus : ICommandBase
    {
        private readonly DownloadOrchestrator _orchestrator;

        public CommandDownloadStatus(DownloadOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        public string Command => "download_status";
        public bool NeedsAdmin => false;

        public async Task Execute(ITelegramBotService botService, Message message, bool isAdmin, CancellationToken cancellationToken)
        {
            var downloads = _orchestrator.GetAllDownloads()
                .Where(d => d.ChatId == message.Chat.Id)
                .OrderByDescending(d => d.StartedAt)
                .Take(10); // Limit to 10 most recent downloads

            if (!downloads.Any())
            {
                await botService.BotClientWrapper.Client.SendTextMessageAsync(
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
                var eta = download.Status == Classes.Models.DownloadStatus.Downloading ?
                    $" (ETA: {TimeSpan.FromMinutes(download.ProgressPercentage > 0 ? (100 - download.ProgressPercentage) / download.ProgressPercentage * (DateTime.UtcNow - download.StartedAt).TotalMinutes : 0):hh\\:mm})" : "";

                sb.AppendLine($"<b>{download.Title} ({download.Year})</b>");
                sb.AppendLine($"Status: <i>{download.Status}</i> {download.ProgressPercentage:F1}%{eta}");
                if (!string.IsNullOrEmpty(download.ErrorMessage))
                {
                    sb.AppendLine($"Error: <pre>{download.ErrorMessage}</pre>");
                }
                sb.AppendLine($"Started: {download.StartedAt:g}");
                sb.AppendLine($"ID: <code>{download.Id}</code>");
                sb.AppendLine("---");
            }

            await botService.BotClientWrapper.Client.SendTextMessageAsync(
                message.Chat.Id,
                sb.ToString(),
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                cancellationToken: cancellationToken);
        }
    }
}
