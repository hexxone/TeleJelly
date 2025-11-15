using Telegram.Bot;

namespace Jellyfin.Plugin.TeleJelly.Services;

/// <summary>
///     DI-Singleton class for holding the reference to the initialized Client.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class TelegramBotClientWrapper
{
    /// <summary>
    ///     DI-Singleton global initialized Client.
    /// </summary>
    public ITelegramBotClient? Client { get; set; }
}
