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
    /// </summary>
    public bool SyncUserNames { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether the Group should be notified about new available Content in the enabled folders.
    ///     TODO actually create a notification event handler/service and listen for new content on all libraries,
    ///      then filter for those where group has access to notify.
    ///      Service should also wait to send the notification until all available metadata has been loaded, especially an image.
    ///      Message should contain the image, Title (Movie Name or Show + Season + Episode), Release Year, Audio and Subtitle languages and IMDB Link
    /// </summary>
    public bool NotifyNewContent { get; set; } = true;
}
