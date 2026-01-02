using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands
{
    public class CommandDownloadCancel : ICommandBase
    {
        private readonly DownloadOrchestrator _orchestrator;

        public CommandDownloadCancel(DownloadOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        public string Command => "download_cancel";
        public bool NeedsAdmin => false;

        public async Task Execute(ITelegramBotService botService, Message message, bool isAdmin, CancellationToken cancellationToken)
        {
            var args = message.Text.Split(' ', 2);
            if (args.Length < 2 || !Guid.TryParse(args[1], out var downloadId))
            {
                await botService.BotClientWrapper.Client.SendTextMessageAsync(
                    message.Chat.Id,
                    "<b>Usage:</b> /download_cancel &lt;download_id&gt;",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                    cancellationToken: cancellationToken);
                return;
            }

            var download = _orchestrator.GetDownload(downloadId);
            if (download == null || download.ChatId != message.Chat.Id)
            {
                await botService.BotClientWrapper.Client.SendTextMessageAsync(
                    message.Chat.Id,
                    "Download not found.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await _orchestrator.UpdateDownloadStatus(downloadId, Classes.Models.DownloadStatus.Canceled);

                await botService.BotClientWrapper.Client.SendTextMessageAsync(
                    message.Chat.Id,
                    $"Successfully canceled download for <b>{download.Title}</b>.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await botService.BotClientWrapper.Client.SendTextMessageAsync(
                    message.Chat.Id,
                    $"Failed to cancel download: {ex.Message}",
                    cancellationToken: cancellationToken);
            }
        }
    }
}
