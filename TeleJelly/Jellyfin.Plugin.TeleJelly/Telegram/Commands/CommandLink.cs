using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Command for linking a Jellyfin Group to a Telegram group.
/// </summary>
// ReSharper disable once UnusedType.Global
public class CommandLink : ICommandBase
{
    /// <summary>
    ///     Gets what command to trigger on.
    /// </summary>
    public string Command => "link";

    /// <summary>
    ///     Gets a value indicating whether this command can only be run as Admin.
    /// </summary>
    public bool NeedsAdmin => true;

    /// <summary>
    ///     The action code to trigger for the Command.
    /// </summary>
    public async Task Execute(TelegramBotService telegramBotService, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var botClient = telegramBotService._client;
        if (message.Chat.Type == ChatType.Private)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                isAdmin ? Constants.PrivateAdminWelcomeMessage : Constants.PrivateUserWelcomeMessage,
                cancellationToken: cancellationToken);

            return;
        }

        var parts = message.Text!.Split(' ');
        if (parts.Length != 2)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                "Usage: /link <telejelly_group_name>",
                cancellationToken: cancellationToken);

            return;
        }

        var groupName = parts[1];
        var group = telegramBotService._config.TelegramGroups.FirstOrDefault(g => g.GroupName == groupName);
        if (group == null)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                $"TeleJelly group '{groupName}' not found",
                cancellationToken: cancellationToken);

            return;
        }

        group.TelegramGroupChat = new TelegramGroupChat { TelegramChatId = message.Chat.Id, SyncUserNames = true, NotifyNewContent = true, };

        // Manually test saving the config by:
        // 1. Linking a group using the `/link` command.
        // 2. Verifying that the plugin's configuration file is updated with the new group link.
        TeleJellyPlugin.Instance!.SaveConfiguration(telegramBotService._config);

        await botClient.SendMessage(
            message.Chat.Id,
            $"Linked this Telegram group to TeleJelly group '{groupName}'",
            cancellationToken: cancellationToken);
    }
}
