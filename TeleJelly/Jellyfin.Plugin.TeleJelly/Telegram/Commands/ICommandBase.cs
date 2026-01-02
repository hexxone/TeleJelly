using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands
{
    public interface ICommandBase
    {
        string Command { get; }
        bool NeedsAdmin { get; }
        Task Execute(ITelegramBotService botService, Message message, bool isAdmin, CancellationToken cancellationToken);
    }
}
