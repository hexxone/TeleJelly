namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     Optionally linked Telegram Chat and its related settings.
/// </summary>
public class TelegramGroupChat
{
    /// <summary>
    ///     Gets or sets the Group-Id which is linked to the parent TeleJelly group.
    /// </summary>
    public long TelegramChatId { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the UserName-list should be kept in sync with Telegram.
    ///     TODO add to Config page?
    /// </summary>
    public bool SyncUserNames { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether the Group should be notified about new available Content in the enabled folders.
    ///     TODO add to Config page?
    /// </summary>
    public bool NotifyNewContent { get; set; } = true;
}
