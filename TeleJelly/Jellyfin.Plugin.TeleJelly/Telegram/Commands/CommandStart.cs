using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Command for printing the info message and/or linking the bot initially.
/// </summary>
// ReSharper disable once UnusedType.Global
public class CommandStart : ICommandBase
{
    /// <summary>
    ///     Gets what command to trigger on.
    /// </summary>
    public string Command => "start";

    /// <summary>
    ///     Gets a value indicating whether this command can only be run as Admin.
    /// </summary>
    public bool NeedsAdmin => false;

    /// <summary>
    ///     The action code to trigger for the Command.
    /// </summary>
    public async Task Execute(TelegramBotService telegramBotService, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var botClient = telegramBotService._client;
        switch (message.Chat.Type)
        {
            case ChatType.Group or ChatType.Supergroup:
                await HandleGroupMessage(telegramBotService, message, isAdmin, cancellationToken);
                break;

            case ChatType.Private:
                await botClient.SendMessage(
                    message.Chat.Id,
                    Constants.PrivateAdminWelcomeMessage,
                    cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task HandleGroupMessage(TelegramBotService telegramBotService,
        Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var botClient = telegramBotService._client;

        var linkedGroup = telegramBotService._config.TelegramGroups.FirstOrDefault(g =>
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
            try
            {
                telegramBotService._logger.LogInformation("Processing start-group parameter: Chat={ChatId} Msg='{Msg}'", message.Chat.Id, message.Text);

                // fixes broken encoded input strings so they can be converted by C#
                var parameter = message.Text.Split(' ', 2)[1];
                var mod4 = parameter.Length % 4;
                if (mod4 > 0)
                {
                    parameter += new string('=', 4 - mod4);
                }

                // Decode the base64 parameter
                var decodedText = Encoding.UTF8.GetString(Convert.FromBase64String(parameter));

                // Check if it matches the expected format "link <groupname>" and User is admin.
                if (decodedText.StartsWith(Constants.LinkPrefix, StringComparison.OrdinalIgnoreCase) && isAdmin)
                {
                    var groupName = decodedText.Substring(Constants.LinkPrefix.Length);

                    await TryLinkGroup(telegramBotService, message.Chat.Id, groupName, cancellationToken);
                    return;
                }
            }
            catch (Exception ex)
            {
                // Log error but don't expose it to users
                telegramBotService._logger.LogError("Error processing start parameter: {Msg}", ex.Message);
            }
        }

        // 3. Else: Print info message for unlinked group
        await botClient.SendMessage(
            message.Chat.Id,
            Constants.GroupWelcomeMessage,
            cancellationToken: cancellationToken);
    }

    private async Task TryLinkGroup(TelegramBotService telegramBotService,
        long chatId, string groupName, CancellationToken cancellationToken)
    {
        var botClient = telegramBotService._client;

        // Find the group by name
        var group = telegramBotService._config.TelegramGroups.FirstOrDefault(g => g.GroupName == groupName);

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
        TeleJellyPlugin.Instance!.SaveConfiguration(telegramBotService._config);

        await botClient.SendMessage(chatId,
            $"Successfully linked this Telegram group to TeleJelly group '{groupName}'.",
            cancellationToken: cancellationToken);
    }
}
