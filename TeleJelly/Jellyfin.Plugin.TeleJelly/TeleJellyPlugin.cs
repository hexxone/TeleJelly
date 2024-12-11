using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.TeleJelly;

/// <summary>
///     Main SSO plugin class.
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

        // var cacheOptions = new MemoryCacheOptions() { ExpirationScanFrequency = TimeSpan.FromMinutes(1) };
        // MemoryCache = new MemoryCache(cacheOptions);
    }

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
    ///     Gets the available internal web pages of this plugin.
    /// </summary>
    /// <returns>A list of internal webpages in this application.</returns>
    IEnumerable<PluginPageInfo> IHasWebPages.GetPages()
    {
        return
        [
            new PluginPageInfo { Name = Name, EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Config.config.html" },
            new PluginPageInfo { Name = Name + ".js", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Config.config.js" },
            new PluginPageInfo { Name = Name + ".css", EmbeddedResourcePath = $"{typeof(TeleJellyPlugin).Namespace}.Assets.Config.config.css" }
        ];
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="configuration"></param>
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        base.UpdateConfiguration(configuration);

    }
}
