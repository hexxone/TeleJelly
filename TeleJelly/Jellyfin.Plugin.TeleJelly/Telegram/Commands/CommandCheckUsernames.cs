using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

internal class CommandCheckUsernames(TelegramBotService telegramBotService) : CommandBase(telegramBotService)
{
    internal override string Command => "check_usernames";

    internal override bool NeedsAdmin => true;


    internal override async Task Execute(ITelegramBotClient botClient,
        Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var members = await botClient.GetChatAdministrators(message.Chat.Id, cancellationToken);
        var missingUsernames = members
            .Where(m => string.IsNullOrEmpty(m.User.Username))
            .Select(m => $"{m.User.FirstName} {m.User.LastName}")
            .ToArray();

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
}
