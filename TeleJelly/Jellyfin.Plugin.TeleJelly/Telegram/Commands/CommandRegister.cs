using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Command for checking if all admin usernames from Telegram are actually registered and if not, registering them.
/// </summary>
// ReSharper disable once UnusedType.Global
public class CommandRegister : ICommandBase
{
    /// <summary>
    ///     Gets what command to trigger on.
    /// </summary>
    public string Command => "register";

    /// <summary>
    ///     Gets a value indicating whether this command can only be run as Admin.
    /// </summary>
    public bool NeedsAdmin => false;


    /// <summary>
    ///     The action code to trigger for the Command.
    /// </summary>
    public async Task Execute(TelegramBotService telegramBotService,
        Message message, bool isAdmin, CancellationToken cancellationToken)
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

        var linkedGroup = telegramBotService._config.TelegramGroups.FirstOrDefault(g =>
            g.TelegramGroupChat != null && g.TelegramGroupChat.TelegramChatId == message.Chat.Id);

        if (linkedGroup != null)
        {
            if (isAdmin)
            {
                var members = await botClient.GetChatAdministrators(message.Chat.Id, cancellationToken);
                var missingUsernames = members
                    .Where(m => string.IsNullOrEmpty(m.User.Username))
                    .Select(m => $"{m.User.FirstName} {m.User.LastName}")
                    .ToArray();

                // TODO instead, add all people with usernames to the group.
                //  then print who got added, who is already on the list from these names and who doesnt have a name.

                if (missingUsernames.Any())
                {
                    await botClient.SendMessage(
                        message.Chat.Id,
                        "The following members need to set a username to use TeleJelly SSO:\n" +
                        string.Join("\n", missingUsernames),
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(
                        message.Chat.Id,
                        "All members have usernames set",
                        cancellationToken: cancellationToken);
                }
            }
            else
            {
                // TODO try to self-add the user.
            }
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
