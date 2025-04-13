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
    ///     Gets or sets the Token Used for validating Telegram login credentials and running the Bot.
    /// </summary>
    public string BotToken { get; set; } = Constants.DefaultBotToken;

    /// <summary>
    ///     Gets or sets the Username of the Bot.
    ///     Is used for the Telegram Login widget.
    ///     Should get set automatically after a valid Bot Token was entered.
    /// </summary>
    public string BotUsername { get; set; } = "MyTelegramBot";

    /// <summary>
    ///     Gets or sets a value indicating whether the Telegram Bot Background Service should be running.
    ///     In order to restart the Service, just disable it, Save, re-enable it and Save again.
    /// </summary>
    public bool EnableBotService { get; set; } = true;

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
    ///     Gets or sets the externally forced URL Scheme.
    ///  !!! IMPORTANT !!!
    ///     Only gets used when either "http" or "https".
    ///     All other values will be ignored (e.g. empty "", or "None")
    /// </summary>
    public string ForcedUrlScheme { get; set; } = "none";

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
