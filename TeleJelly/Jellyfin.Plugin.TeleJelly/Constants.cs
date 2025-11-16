using System;
using Jellyfin.Plugin.TeleJelly.Classes;

namespace Jellyfin.Plugin.TeleJelly;

/// <summary>
///     Constants for this specific Plugin.
/// </summary>
public static class Constants
{
    /// <summary>
    ///     Gets The main GUID of the Plugins.
    ///     Also needs to be updated in the "meta.xml" and "config.js".
    /// </summary>
    public static Guid Id => Guid.Parse("4b71013d-00ba-470c-9e4d-0c451a435328");

    /// <summary>
    ///     Gets the name of the SSO plugin.
    /// </summary>
    public static string PluginName => "TeleJelly";

    /// <summary>
    ///     Gets the name of the data Folder.
    ///     Currently only used for storing user images.
    /// </summary>
    public static string PluginDataFolder => "data";

    /// <summary>
    ///     Gets the name of the data Folder.
    ///     Currently only used for storing user images.
    /// </summary>
    public static string UserImageFolder => "userimages";

    /// <summary>
    ///     Gets which ExtraFile to use as default, if the Telegram User has not set one.
    /// </summary>
    public static string DefaultUserImageExtraFile => "TeleJellyLogo.jpg";

    /// <summary>
    ///     Gets the default placeholder Bot Token.
    /// </summary>
    public static string DefaultBotToken => "12345678:xxxxxxxxxxxxxxx";


    internal const string GroupWelcomeMessage =
        "Welcome to TeleJelly! This group is not linked yet.\n\n" +
        "An administrator can do this using the /link command.";

    internal const string PrivateAdminWelcomeMessage =
        "Welcome to TeleJelly! I can help you and your friends to authenticate with your Jellyfin server using Telegram.\n" +
        "To get started, add me to a group and use the /link command to connect it to your TeleJelly group.\n" +
        "For more information, please check the TeleJelly documentation.";

    internal const string PrivateUserWelcomeMessage =
        "Welcome to TeleJelly! Unfortunately you are not an Administrator.\n\n" +
        "Please interact with me through a linked group.";

    internal const string LinkPrefix = "l:";

    /// <summary>
    ///     Gets the always available list of extra files for Telegram SSO.
    ///     e.g. Fonts and CSS.
    ///     has replaceable params:
    ///     - {{SERVER_URL}} = Jellyfin base Url
    ///     - {{JELLYFIN_DEFAULT_LOGIN}} = Fallback Login url
    ///     - {{TELEGRAM_BOT_NAME}} = Bot Username.
    /// </summary>
    /// <returns>A list of internal webpages in this application.</returns>
    public static readonly ExtraPageInfo[] LoginFiles =
    [
        new() { Name = "index", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Login.login.html", NeedsReplacement = true },
        new() { Name = "login.css", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Login.login.css", NeedsReplacement = true },
        new() { Name = "login.js", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Login.login.js", NeedsReplacement = true },
        new() { Name = "material_icons.woff2", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Login.material_icons.woff2" },
        new() { Name = DefaultUserImageExtraFile, EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Login.TeleJellyLogo.png" }
    ];
}
