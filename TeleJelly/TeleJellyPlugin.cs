using System;
using System.Collections.Generic;
using Jellyfin.Plugin.TeleJelly.Classes;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.TeleJelly;

/// <summary>
///     Main SSO plugin class.
/// </summary>
public class TeleJellyPlugin : BasePlugin<PluginConfiguration>, IPlugin, IHasWebPages
{
    /// <summary>
    ///     Gets the always available list of extra files for Telegram SSO.
    ///     e.g. Fonts and CSS.
    ///     has replaceable params:
    ///     - {{SERVER_URL}} = Jellyfin base Url
    ///     - {{JELLYFIN_DEFAULT_LOGIN}} = Fallback Login url
    ///     - {{TELEGRAM_BOT_NAME}} = Bot Username.
    /// </summary>
    /// <returns>A list of internal webpages in this application.</returns>
    public static readonly ExtraPageInfo[] LoginFiles = { new() { Name = "index", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Login.login.html", NeedsReplacement = true }, new() { Name = "login.css", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Login.login.css", NeedsReplacement = true }, new() { Name = "login.js", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Login.login.js", NeedsReplacement = true }, new() { Name = "material_icons.woff2", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Login.material_icons.woff2" }, new() { Name = Constants.DefaultUserImageExtraFile, EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Login.TeleJellyLogo.jpg" }, };

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

        var cacheOptions = new MemoryCacheOptions() { ExpirationScanFrequency = TimeSpan.FromMinutes(1) };
        MemoryCache = new MemoryCache(cacheOptions);
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
    ///     Gets the In-memory cache for the Plugin html pages.
    /// </summary>
    public IMemoryCache MemoryCache { get; }

    /// <summary>
    ///     Gets the name of the SSO plugin.
    /// </summary>
    public override string Name => Constants.PluginName;

    /// <summary>
    ///     Gets the GUID of the SSO plugin.
    /// </summary>
    public override Guid Id => Constants.Id;

    /// <summary>
    ///     Returns the available internal web pages of this plugin.
    /// </summary>
    /// <returns>A list of internal webpages in this application.</returns>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new PluginPageInfo[] { new() { Name = Name, EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Config.config.html" }, new() { Name = Name + ".js", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Config.config.js" }, new() { Name = Name + ".css", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Config.config.css" } };
    }
}
