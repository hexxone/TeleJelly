using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

public class CommandDownloadSetPath : ICommandBase
{
    private readonly IDownloadOrchestrator _orchestrator;

    public CommandDownloadSetPath(IDownloadOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public string Command => "download_setpath";
    public bool NeedsAdmin => true;

    public async Task Execute(ITelegramBotService botService, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var client = botService.BotClientWrapper.Client;
        if (client == null)
        {
            botService.Logger.LogError("Telegram Bot Client wrapper is null in CommandDownloadSetPath.");
            return;
        }

        var args = message.Text?.Split(' ', 3) ?? [];
        if (args.Length < 3 || !Guid.TryParse(args[1], out var downloadId))
        {
            await client.SendMessage(
                message.Chat.Id,
                "<b>Usage:</b> /download_setpath &lt;download_id&gt; &lt;path&gt;\n\n" +
                "Example: /download_setpath 12345678-1234-1234-1234-123456789012 /media/movies/MyMovie",
                ParseMode.Html,
                cancellationToken: cancellationToken);
            return;
        }

        var customPath = args[2].Trim();
        var download = _orchestrator.GetDownload(downloadId);
        if (download == null || download.ChatId != message.Chat.Id)
        {
            await client.SendMessage(
                message.Chat.Id,
                "Download not found.",
                cancellationToken: cancellationToken);
            return;
        }

        // Validate path
        if (!Path.IsPathFullyQualified(customPath))
        {
            await client.SendMessage(
                message.Chat.Id,
                "❌ Path must be an absolute path (e.g., /media/movies/MyMovie).",
                cancellationToken: cancellationToken);
            return;
        }

        try
        {
            // Update the download's destination path
            download.SuggestedDestinationPath = customPath;
            download.UserConfirmedPath = customPath;

            // If download is in a state where we can initiate it, do so
            if (download.Status == DownloadStatus.AwaitingPathConfirm)
            {
                await _orchestrator.UpdateDownloadStatus(downloadId, DownloadStatus.AwaitingPathConfirm);
                var success = await _orchestrator.InitiateDownloadAsync(downloadId, cancellationToken);

                if (success)
                {
                    var statusMessage = await client.SendMessage(
                        message.Chat.Id,
                        $"✅ Path updated and download started for <b>{download.Title}</b>!\n" +
                        $"Path: <code>{customPath}</code>",
                        ParseMode.Html,
                        cancellationToken: cancellationToken);
                    await _orchestrator.SetTelegramMessageAsync(download.Id, statusMessage.MessageId, statusMessage.Date.ToUniversalTime(), cancellationToken);
                }
                else
                {
                    var failureMessage = await client.SendMessage(
                        message.Chat.Id,
                        $"✅ Path updated, but failed to start download.\n" +
                        $"Error: {System.Net.WebUtility.HtmlEncode(DownloadFailureGuidance.AppendReplyOption(download.ErrorMessage ?? "No available download service."))}",
                        ParseMode.Html,
                        cancellationToken: cancellationToken);
                    await botService.RegisterFailedDownloadReplyAsync(failureMessage.MessageId, download.Id, cancellationToken);
                }
            }
            else
            {
                await client.SendMessage(
                    message.Chat.Id,
                    $"✅ Path updated for <b>{download.Title}</b>.\n" +
                    $"Path: <code>{customPath}</code>\n" +
                    $"Status: {download.Status}",
                    ParseMode.Html,
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            botService.Logger.LogError(ex, "Failed to set path for download {DownloadId}", downloadId);
            await _orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.Failed, ex.Message);
            var failureMessage = await client.SendMessage(
                message.Chat.Id,
                $"❌ {DownloadFailureGuidance.AppendReplyOption(download.ErrorMessage ?? ex.Message)}",
                cancellationToken: cancellationToken);
            await botService.RegisterFailedDownloadReplyAsync(failureMessage.MessageId, download.Id, cancellationToken);
        }
    }
}
