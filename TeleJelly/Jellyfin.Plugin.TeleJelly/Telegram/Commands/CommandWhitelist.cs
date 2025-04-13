using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

internal class CommandWhitelist(TelegramBotService telegramBotService) : CommandBase(telegramBotService)
{
    internal override string Command => "whitelist";
    internal override bool NeedsAdmin => true;

    internal override async Task Execute(ITelegramBotClient botClient, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var group = _telegramBotService._config.TelegramGroups.Find(g => g.TelegramGroupChat?.TelegramChatId == message.Chat.Id);
        if (group != null)
        {
            var users = string.Join("\n", group.UserNames.Select(u => $"@{u}"));
            await botClient.SendMessage(
                message.Chat.Id,
                $"Users in whitelist for group '{group.GroupName}':\n{users}",
                cancellationToken: cancellationToken);
        }
    }
}
