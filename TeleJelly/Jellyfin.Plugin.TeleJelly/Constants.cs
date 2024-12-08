using System;

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
}
