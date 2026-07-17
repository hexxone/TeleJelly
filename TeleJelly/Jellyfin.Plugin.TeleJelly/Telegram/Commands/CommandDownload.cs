using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

public class CommandDownload : ICommandBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IDownloadOrchestrator _orchestrator;

    public CommandDownload(IDownloadOrchestrator orchestrator, ILibraryManager libraryManager)
    {
        _orchestrator = orchestrator;
        _libraryManager = libraryManager;
    }

    public string Command => "download";
    public bool NeedsAdmin => false;

    public async Task Execute(ITelegramBotService botService, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var client = botService.BotClientWrapper.Client;
        if (client == null)
        {
            botService.Logger.LogError("Telegram Bot Client wrapper is null in CommandLink.");
            return;
        }

        var downloadManagerConfig = TeleJellyPlugin.Instance?.Configuration.DownloadManager;
        if (downloadManagerConfig?.Enabled != true)
        {
            await client.SendMessage(
                message.Chat.Id,
                "Download manager is disabled.",
                cancellationToken: cancellationToken);
            return;
        }

        if (downloadManagerConfig.WhitelistUsernames.Count > 0)
        {
            var username = message.From?.Username;
            var isWhitelisted = !string.IsNullOrWhiteSpace(username) &&
                                downloadManagerConfig.WhitelistUsernames.Contains(username, StringComparer.OrdinalIgnoreCase);
            if (!isWhitelisted)
            {
                await client.SendMessage(
                    message.Chat.Id,
                    "You are not allowed to use the download manager.",
                    cancellationToken: cancellationToken);
                return;
            }
        }

        var args = message.Text?.Split(' ', 3) ?? [];
        if (args.Length < 2)
        {
            await client.SendMessage(
                message.Chat.Id,
                "<b>Usage:</b> /download &lt;imdb_id&gt; [link_or_magnet]",
                ParseMode.Html,
                cancellationToken: cancellationToken);
            return;
        }

        var imdbId = args[1];
        var link = args.Length > 2 ? args[2] : null;

        if (link == null && !downloadManagerConfig.Search.Enabled)
        {
            await client.SendMessage(
                message.Chat.Id,
                "Automated search is disabled. Provide a direct link or enable search first.",
                cancellationToken: cancellationToken);
            return;
        }

        if (!imdbId.StartsWith("tt"))
        {
            await client.SendMessage(
                message.Chat.Id,
                "Invalid IMDB ID. It should start with 'tt'.",
                cancellationToken: cancellationToken);
            return;
        }

        if (message.From == null)
        {
            await client.SendMessage(
                message.Chat.Id,
                "Cannot identify user.",
                cancellationToken: cancellationToken);
            return;
        }

        ManagedDownload? download = null;
        var libraryCount = 0;
        try
        {
            download = await _orchestrator.BeginDownloadWorkflow(imdbId, message.Chat.Id, message.From.Id, link);

            libraryCount = _libraryManager.GetVirtualFolders().Count();
            await botService.StartDownloadSelectionAsync(download, cancellationToken);
        }
        catch (Exception ex)
        {
            botService.Logger.LogError(
                ex,
                "Failed to start download for IMDB ID {ImdbId}. DownloadId: {DownloadId}, UserId: {UserId}, ChatId: {ChatId}, LibraryCount: {LibraryCount}, HasDirectLink: {HasDirectLink}",
                imdbId,
                download?.Id,
                message.From.Id,
                message.Chat.Id,
                libraryCount,
                !string.IsNullOrWhiteSpace(link));

            if (download != null)
            {
                await _orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.Failed, ex.Message);
            }

            var failureText = download?.ErrorMessage ??
                              DownloadFailureGuidance.Append($"Failed to start download: {ex.Message}", imdbId, link);
            if (download != null)
            {
                failureText = DownloadFailureGuidance.AppendReplyOption(failureText);
            }

            var failureMessage = await client.SendMessage(
                message.Chat.Id,
                $"❌ {failureText}",
                cancellationToken: cancellationToken);
            if (download != null)
            {
                await botService.RegisterFailedDownloadReplyAsync(failureMessage.MessageId, download.Id, cancellationToken);
            }
        }
    }
}
