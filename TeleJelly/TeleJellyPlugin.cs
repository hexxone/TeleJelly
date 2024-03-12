using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

// TODO [assembly:CLSCompliant(true)]

namespace Jellyfin.Plugin.TeleJelly;

/// <summary>
///     The SSO plugin class.
/// </summary>
public class TeleJellyPlugin : BasePlugin<PluginConfiguration>, IPlugin, IHasWebPages
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TeleJellyPlugin" /> class.
    /// </summary>
    /// <param name="applicationPaths">Internal Jellyfin interface for the ApplicationPath.</param>
    /// <param name="xmlSerializer">Internal Jellyfin interface for the XML information.</param>
    public TeleJellyPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        ApplicationPaths = applicationPaths;
        Instance = this;
    }

    /// <summary>
    ///     Gets the instance of the SSO plugin.
    /// </summary>
    public static TeleJellyPlugin? Instance { get; private set; }

    /// <summary>
    ///     Gets the Runtime Jellyfin Application Path provider.
    /// </summary>
    public new IApplicationPaths ApplicationPaths { get; }

    /// <summary>
    ///     Gets the name of the SSO plugin.
    /// </summary>
    public override string Name => Constants.PluginName;

    /// <summary>
    ///     Gets the GUID of the SSO plugin.
    /// </summary>
    public override Guid Id => Constants.Id;

    /// <summary>
    ///     Gets the Resource-Info for the Telegram Auth Page.
    ///     has replaceable params:
    ///     - {{SERVER_URL}} = Jellyfin base Url
    ///     - {{JELLYFIN_DEFAULT_LOGIN}} = Fallback Login url
    ///     - {{TELEGRAM_BOT_NAME}} = Bot Username.
    /// </summary>
    public PluginPageInfo TelegramLoginPage => new() { Name = "login", EmbeddedResourcePath = $"{GetType().Namespace}.Pages.telegram.login.html" };

    /// <summary>
    ///     Returns the available internal web pages of this plugin.
    /// </summary>
    /// <returns>A list of internal webpages in this application.</returns>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new PluginPageInfo[] { new() { Name = Name, EmbeddedResourcePath = $"{GetType().Namespace}.Config.configPage.html" }, new() { Name = Name + ".js", EmbeddedResourcePath = $"{GetType().Namespace}.Config.config.js" }, new() { Name = Name + ".css", EmbeddedResourcePath = $"{GetType().Namespace}.Config.style.css" } };
    }

    /// <summary>
    ///     Gets the always available list of extra files for Telegram SSO.
    ///     e.g. Fonts and CSS.
    /// </summary>
    /// <returns>A list of internal webpages in this application.</returns>
    public IEnumerable<PluginPageInfo> GetExtraFiles()
    {
        return new PluginPageInfo[]
        {
            new() { Name = "login.css", EmbeddedResourcePath = $"{GetType().Namespace}.Pages.Files.login.css" },
            new() { Name = "login.js", EmbeddedResourcePath = $"{GetType().Namespace}.Pages.Files.login.js" },
            new() { Name = "material_icons.woff2", EmbeddedResourcePath = $"{GetType().Namespace}.Pages.Files.material_icons.woff2" },
            new() { Name = Constants.DefaultUserImageExtraFile, EmbeddedResourcePath = $"{GetType().Namespace}.Pages.Files.TeleJellyLogo.jpg" },
        };
    }
}
