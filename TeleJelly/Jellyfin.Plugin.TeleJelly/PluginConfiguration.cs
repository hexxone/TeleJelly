using System.Collections.Generic;
using System.Xml.Serialization;
using Jellyfin.Plugin.TeleJelly.Telegram;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TeleJelly;

/// <summary>
///     Customized Plugin Configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    ///     Gets or sets a value Used for validating user login Data hashes.
    /// </summary>
    public string BotToken { get; set; } = Constants.DefaultBotToken;

    /// <summary>
    ///     Gets or sets a value indicating the value Used for the Telegram Login widget...
    /// </summary>
    public string BotUsername { get; set; } = "MyTelegramBot";

    /// <summary>
    ///     Gets or sets a value indicating the List of users to grant admin permissions.
    ///     Be careful! Usernames in Telegram can be sold, bought and changed easily.
    /// </summary>
    public List<string> AdminUserNames { get; set; } = new();

    /// <summary>
    ///     Gets or sets a Hard Cap for Login Sessions for User with Telegram..
    /// </summary>
    public int MaxSessionCount { get; set; } = -1;

    /// <summary>
    ///     Gets or sets a value indicating whether we should force a Specific protocol Scheme on externally returned URLS ("ForcedUrlScheme").
    ///     This is probably useful if your jellyfin is running behind a Reverse Proxy which does "SSL-stripping" (like Traefik).
    /// </summary>
    public bool ForceUrlScheme { get; set; } = true;

    /// <summary>
    ///     Gets or sets the externally forced URL Scheme.
    ///     Only gets used when "ForceUrlScheme" is true.
    /// </summary>
    public string ForcedUrlScheme { get; set; } = "https";

    /// <summary>
    ///     Gets or sets List of allowed Telegram user-names for login.
    ///     User not in a group -> CANNOT LOGIN !
    ///     A user can be member of multiple groups.
    ///     A user will be granted the Folders of ALL groups he is member.
    /// </summary>
    [XmlArray("TelegramGroups")]
    [XmlArrayItem(typeof(TelegramGroup), ElementName = "TelegramGroups")]
    public List<TelegramGroup> TelegramGroups { get; set; } = new();
}
