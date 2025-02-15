using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

internal class CommandUnlink(TelegramBotService telegramBotService) : CommandBase(telegramBotService)
{
    internal override string Command => "unlink";
    internal override bool NeedsAdmin => true;

    internal override async Task Execute(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var group = _telegramBotService._config.TelegramGroups.Find(g => g.LinkedTelegramGroupId == message.Chat.Id);
        if (group != null)
        {
            group.LinkedTelegramGroupId = null;
            await botClient.SendMessage(
                message.Chat.Id,
                $"Unlinked this Telegram group from TeleJelly group '{group.GroupName}'",
                cancellationToken: cancellationToken);
        }
    }
}
