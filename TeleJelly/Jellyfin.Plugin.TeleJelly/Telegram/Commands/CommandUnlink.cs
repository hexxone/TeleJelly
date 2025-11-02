using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Command for unlinking a Telegram Group from Jellyfin.
/// </summary>
// ReSharper disable once UnusedType.Global
public class CommandUnlink : ICommandBase
{
    /// <summary>
    ///     Gets what command to trigger on.
    /// </summary>
    public string Command => "unlink";

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

        var group = telegramBotService._config.TelegramGroups.FirstOrDefault(g => g.TelegramGroupChat?.TelegramChatId == message.Chat.Id);
        if (group != null)
        {
            group.TelegramGroupChat = null;

            // Manually test saving the config by:
            // 1. Unlinking a group using the `/unlink` command.
            // 2. Verifying that the plugin's configuration file is updated to remove the group link.
            TeleJellyPlugin.Instance!.SaveConfiguration(telegramBotService._config);

            await botClient.SendMessage(
                message.Chat.Id,
                $"Unlinked this Telegram group from TeleJelly group '{group.GroupName}'",
                cancellationToken: cancellationToken);
        }
        else
        {
            await botClient.SendMessage(
                message.Chat.Id,
                Constants.GroupWelcomeMessage,
                cancellationToken: cancellationToken);
        }
    }
}
