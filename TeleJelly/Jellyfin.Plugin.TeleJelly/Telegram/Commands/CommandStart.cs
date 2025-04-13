using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

internal class CommandStart(TelegramBotService telegramBotService) : CommandBase(telegramBotService)
{
    internal override string Command => "start";
    internal override bool NeedsAdmin => false;

    internal override async Task Execute(ITelegramBotClient botClient, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        switch (message.Chat.Type)
        {
            case ChatType.Group or ChatType.Supergroup:
                await HandleGroupMessage(botClient, message, isAdmin, cancellationToken);
                break;

            case ChatType.Private:
                // IN PM -> Print info message
                await botClient.SendMessage(
                    message.Chat.Id,
                    "Welcome to TeleJelly! I can help you authenticate with your Jellyfin server using Telegram.\n\n" +
                    "To get started, add me to a group and use the /link command to connect it to your Jellyfin server.\n\n" +
                    "For more information, please check the TeleJelly documentation or contact your Jellyfin administrator.",
                    cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task HandleGroupMessage(ITelegramBotClient botClient, Message message, bool isAdmin,
        CancellationToken cancellationToken)
    {
        var linkedGroup = _telegramBotService._config.TelegramGroups.Find(g =>
            g.TelegramGroupChat != null && g.TelegramGroupChat.TelegramChatId == message.Chat.Id);

        // 1. If Group already linked, print info message
        if (linkedGroup != null)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                $"This group is already linked to TeleJelly group '{linkedGroup.GroupName}'.",
                cancellationToken: cancellationToken);
            return;
        }

        // 2. If Group unlinked and has startgroup parameter
        if (message.Text != null && message.Text.Contains(' '))
        {
            var parameter = message.Text.Split(' ', 2)[1];
            if (parameter.Length <= 64)
            {
                try
                {
                    // Decode the base64 parameter
                    var decodedBytes = Convert.FromBase64String(parameter);
                    var decodedText = Encoding.UTF8.GetString(decodedBytes);

                    // Check if it matches the expected format "link <groupname>" and User is admin..
                    if (decodedText.StartsWith("link ", StringComparison.OrdinalIgnoreCase) && isAdmin)
                    {
                        var groupName = decodedText.Substring(5); // Remove "link " prefix

                        await TryLinkGroup(botClient, message.Chat.Id, groupName, cancellationToken);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Log error but don't expose it to users
                    _telegramBotService._logger.LogError("Error processing start parameter: {Msg}", ex.Message);
                }
            }
        }

        // 3. Else: Print info message for unlinked group
        await botClient.SendMessage(
            message.Chat.Id,
            "Welcome to TeleJelly! This group is not linked to any TeleJelly group yet.\n\n" +
            "An administrator can link this group using the /link command.",
            cancellationToken: cancellationToken);
    }

    private async Task TryLinkGroup(ITelegramBotClient botClient, long chatId,
        string groupName, CancellationToken cancellationToken)
    {
        // Find the group by name
        var group = _telegramBotService._config.TelegramGroups.Find(g => g.GroupName == groupName);

        if (group == null)
        {
            await botClient.SendMessage(chatId,
                $"TeleJelly group '{groupName}' not found.",
                cancellationToken: cancellationToken);
            return;
        }

        // Link the group
        group.TelegramGroupChat = new TelegramGroupChat { TelegramChatId = chatId, SyncUserNames = true, NotifyNewContent = true };

        // Save the configuration
        TeleJellyPlugin.Instance!.SaveConfiguration(_telegramBotService._config);

        await botClient.SendMessage(chatId,
            $"Successfully linked this Telegram group to TeleJelly group '{groupName}'.",
            cancellationToken: cancellationToken);
    }
}
