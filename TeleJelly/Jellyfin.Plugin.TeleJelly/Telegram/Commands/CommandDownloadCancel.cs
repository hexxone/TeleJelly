using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

public class CommandDownloadCancel : ICommandBase
{
    private readonly IDownloadOrchestrator _orchestrator;

    public CommandDownloadCancel(IDownloadOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public string Command => "download_cancel";
    public bool NeedsAdmin => true;

    public async Task Execute(ITelegramBotService botService, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var client = botService.BotClientWrapper.Client;
        if (client == null)
        {
            botService.Logger.LogError("Telegram Bot Client wrapper is null in CommandLink.");
            return;
        }

        var args = message.Text?.Split(' ', 2) ?? [];
        if (args.Length < 2 || !Guid.TryParse(args[1], out var downloadId))
        {
            await client.SendMessage(
                message.Chat.Id,
                "<b>Usage:</b> /download_cancel &lt;download_id&gt;",
                ParseMode.Html,
                cancellationToken: cancellationToken);
            return;
        }

        var download = _orchestrator.GetDownload(downloadId);
        if (download == null || download.ChatId != message.Chat.Id)
        {
            await client.SendMessage(
                message.Chat.Id,
                "Download not found.",
                cancellationToken: cancellationToken);
            return;
        }

        try
        {
            await _orchestrator.CancelDownloadAsync(downloadId, cancellationToken);

            await client.SendMessage(
                message.Chat.Id,
                $"Successfully canceled download for <b>{download.Title}</b>.",
                ParseMode.Html,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await client.SendMessage(
                message.Chat.Id,
                $"Failed to cancel download: {ex.Message}",
                cancellationToken: cancellationToken);
        }
    }
}
