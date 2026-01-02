using Telegram.Bot;

namespace Jellyfin.Plugin.TeleJelly.Services;

/// <summary>
///     Dependency injected singleton class for holding the reference to the initialized Telegram Bot Api Client.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class TelegramBotClientWrapper
{
    /// <summary>
    ///     DI-Singleton global initialized Client.
    /// </summary>
    internal ITelegramBotClient? Client { get; set; }
}
