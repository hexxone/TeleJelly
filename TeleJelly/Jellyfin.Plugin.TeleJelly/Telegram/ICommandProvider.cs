using Jellyfin.Plugin.TeleJelly.Telegram.Commands;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     Interface for providing Telegram commands.
/// </summary>
public interface ICommandProvider
{
    /// <summary>
    ///     Gets the registered commands.
    /// </summary>
    ICommandBase[] GetCommands();
}
