using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Jellyfin.Plugin.TeleJelly.Telegram.Bot;

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
            var args = message.Text?.Split(' ', 2) ?? new string[0];
            if (args.Length < 2 || !Guid.TryParse(args[1], out var downloadId))
            {
                await botService.BotClientWrapper.Client.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "<b>Usage:</b> /download_cancel &lt;download_id&gt;",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
                return;
            }

            var download = _orchestrator.GetDownload(downloadId);
            if (download == null || download.ChatId != message.Chat.Id)
            {
                await botService.BotClientWrapper.Client.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Download not found.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                await _orchestrator.UpdateDownloadStatus(downloadId, Classes.Models.DownloadStatus.Canceled);

                await botService.BotClientWrapper.Client.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"Successfully canceled download for <b>{download.Title}</b>.",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await botService.BotClientWrapper.Client.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"Failed to cancel download: {ex.Message}",
                    cancellationToken: cancellationToken);
            }
        }
    }
}
