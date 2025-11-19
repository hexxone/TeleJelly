using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Provides a base-class for custom Telegram Bot Commands.
/// </summary>
internal interface ICommandBase
{
    /// <summary>
    ///     Gets what command to trigger on.
    /// </summary>
    string Command { get; }

    /// <summary>
    ///     Gets a value indicating whether this command can only be run as Admin.
    /// </summary>
    bool NeedsAdmin { get; }

    /// <summary>
    ///     The action code to trigger for the Command.
    /// </summary>
    /// <param name="telegramBotService">Telegram bot Service for sending messages etc.</param>
    /// <param name="message">Received Text message.</param>
    /// <param name="isAdmin"></param>
    /// <param name="cancellationToken">Bot process lifetime token.</param>
    /// <returns></returns>
    Task Execute(TelegramBotService telegramBotService, Message message, bool isAdmin, CancellationToken cancellationToken);
}
