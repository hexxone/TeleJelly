using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Command for printing all users in the current linked group.
/// </summary>
// ReSharper disable once UnusedType.Global
internal class CommandUserlist : ICommandBase
{
    /// <summary>
    ///     Gets what command to trigger on.
    /// </summary>
    public string Command => "userlist";

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
            var users = string.Join("\n", group.UserNames.Select(u => $"@{u}"));
            await botClient.SendMessage(
                message.Chat.Id,
                $"Registered Users in this group ({group.GroupName}):\n{users}",
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
