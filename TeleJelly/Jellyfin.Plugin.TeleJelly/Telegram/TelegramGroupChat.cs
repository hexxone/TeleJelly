namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     Optionally linked Telegram Chat and its related settings.
///     Supports Group, Supergroup, Channel and Private chat types.
/// </summary>
public class TelegramGroupChat
{
    /// <summary>
    ///     Supported chat types for a linked Telegram chat.
    /// </summary>
    public enum TelegramChatType
    {
        Group,
        Supergroup,
        Channel,
        Private
    }

    /// <summary>
    ///     Gets or sets the Chat-Id which is linked to the parent TeleJelly group.
    ///     If this is 0, the group is considered unlinked.
    /// </summary>
    public long TelegramChatId { get; set; }

    /// <summary>
    ///     Gets or sets the Telegram chat type.
    /// </summary>
    public TelegramChatType ChatType { get; set; } = TelegramChatType.Group;

    /// <summary>
    ///     Gets or sets a value indicating whether the UserName-list should be kept in sync with Telegram.
    ///     For channels and private chats, this is typically not applicable and may be ignored.
    /// </summary>
    public bool SyncUserNames { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether the Group should be notified about new available Content in the enabled folders.
    /// </summary>
    public bool NotifyNewContent { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether users in this chat may create requests using /request.
    ///     This option is configurable on the configuration page.
    /// </summary>
    public bool AllowRequests { get; set; } = true;
}
