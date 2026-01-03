using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands
{
    public class CommandDownload : ICommandBase
    {
        private readonly DownloadOrchestrator _orchestrator;
        private readonly ILibraryManager _libraryManager;

        public CommandDownload(DownloadOrchestrator orchestrator, ILibraryManager libraryManager)
        {
            _orchestrator = orchestrator;
            _libraryManager = libraryManager;
        }

        public string Command => "download";
        public bool NeedsAdmin => false;

        public async Task Execute(ITelegramBotService botService, Message message, bool isAdmin, CancellationToken cancellationToken)
        {
            var args = message.Text?.Split(' ', 3) ?? [];
            if (args.Length < 2)
            {
                await botService.BotClientWrapper.Client.SendMessage(
                    chatId: message.Chat.Id,
                    text: "<b>Usage:</b> /download &lt;imdb_id&gt; [link_or_magnet]",
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
                return;
            }

            var imdbId = args[1];
            var link = args.Length > 2 ? args[2] : null;

            if (!imdbId.StartsWith("tt"))
            {
                await botService.BotClientWrapper.Client.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Invalid IMDB ID. It should start with 'tt'.",
                    cancellationToken: cancellationToken);
                return;
            }

            if (message.From == null)
            {
                await botService.BotClientWrapper.Client.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Cannot identify user.",
                    cancellationToken: cancellationToken);
                return;
            }

            try
            {
                var download = await _orchestrator.BeginDownloadWorkflow(imdbId, message.Chat.Id, message.From.Id, link);

                var libraries = _libraryManager.GetUserRootFolder().Children.ToArray();
                if (!libraries.Any())
                {
                    await botService.BotClientWrapper.Client.SendMessage(chatId: message.Chat.Id, text: "No libraries configured in Jellyfin.", cancellationToken: cancellationToken);
                    return;
                }

                var keyboardButtons = libraries
                    .Select(lib => InlineKeyboardButton.WithCallbackData(lib.Name ?? "Unnamed Library", $"dl_{download.Id}_library_{lib.Id}"))
                    .ToList();
                keyboardButtons.Add(InlineKeyboardButton.WithCallbackData("Cancel", $"dl_{download.Id}_cancel"));

                var keyboard = new InlineKeyboardMarkup(keyboardButtons);

                await botService.BotClientWrapper.Client.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"Starting download for <b>{download.Title} ({download.Year})</b>.\n\nPlease select the destination library:",
                    parseMode: ParseMode.Html,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                await botService.BotClientWrapper.Client.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"Failed to start download: {ex.Message}",
                    cancellationToken: cancellationToken);
            }
        }
    }
}
