using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

internal class CommandLink(TelegramBotService telegramBotService) : CommandBase(telegramBotService)
{
    internal override string Command => "link";
    internal override bool NeedsAdmin => true;

    internal override async Task Execute(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
    {
        var parts = message.Text!.Split(' ');
        if (parts.Length != 2)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                "Usage: /link <telejelly_group_name>",
                cancellationToken: cancellationToken);
        }

        var groupName = parts[1];
        var group = _telegramBotService._config.TelegramGroups.Find(g => g.GroupName == groupName);

        if (group == null)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                $"TeleJelly group '{groupName}' not found",
                cancellationToken: cancellationToken);
            return;
        }

        group.LinkedTelegramGroupId = message.Chat.Id;
        await botClient.SendMessage(
            message.Chat.Id,
            $"Linked this Telegram group to TeleJelly group '{groupName}'",
            cancellationToken: cancellationToken);
    }
}
