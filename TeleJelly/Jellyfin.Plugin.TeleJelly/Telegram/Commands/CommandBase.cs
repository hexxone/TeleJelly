using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Provides a base-class for custom Telegram Bot Commands.
/// </summary>
internal abstract class CommandBase
{
    /// <summary>
    ///     Gets the parent Bot service for config access etc.
    /// </summary>
    protected readonly TelegramBotService _telegramBotService;

    /// <summary>
    ///     Gets what command to trigger on.
    /// </summary>
    internal abstract string Command { get; }

    /// <summary>
    ///     Gets a value indicating whether this command can only be run as Admin.
    /// </summary>
    internal abstract bool NeedsAdmin { get; }

    /// <summary>
    ///     Constructs a new instance of the base-Command.
    /// </summary>
    /// <param name="telegramBotService"></param>
    protected CommandBase(TelegramBotService telegramBotService)
    {
        _telegramBotService = telegramBotService;
    }

    /// <summary>
    ///     The action code to trigger for the Command.
    /// </summary>
    /// <param name="botClient">Telegram bot for sending messages etc.</param>
    /// <param name="message">Received Text message.</param>
    /// <param name="isAdmin"></param>
    /// <param name="cancellationToken">Bot process lifetime token.</param>
    /// <returns></returns>
    internal abstract Task Execute(ITelegramBotClient botClient, Message message, bool isAdmin, CancellationToken cancellationToken);
}
