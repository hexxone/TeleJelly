using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using Jellyfin.Plugin.TeleJelly.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly;

/// <summary>
///     Main SSO plugin class.
/// </summary>
public class TeleJellyPlugin : BasePlugin<PluginConfiguration>, IPlugin, IHasWebPages, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly NotificationService _notificationService;

    /// <summary>
    ///     Gets or sets the Plugin Singleton instance.
    /// </summary>
    public static TeleJellyPlugin? Instance { get; private set; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="TeleJellyPlugin" /> class.
    /// </summary>
    /// <param name="logger">startup logger</param>
    /// <param name="applicationPaths">Internal Jellyfin interface for the ApplicationPath.</param>
    /// <param name="xmlSerializer">Internal Jellyfin interface for the XML information.</param>
    /// <param name="libraryManager"></param>
    /// <param name="notificationService"></param>
    public TeleJellyPlugin(
        ILogger<TeleJellyPlugin> logger,
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILibraryManager libraryManager,
        NotificationService notificationService)
        : base(applicationPaths, xmlSerializer)
    {
        ApplicationPaths = applicationPaths;
        Instance = this;
        _libraryManager = libraryManager;
        _notificationService = notificationService;
        _libraryManager.ItemAdded += _notificationService.OnItemAdded;
        _libraryManager.ItemUpdated += _notificationService.OnItemUpdated;
        logger.LogInformation("{PluginName} initialized.", nameof(TeleJellyPlugin));
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
    ///     Releases the resources used by the <see cref="TeleJellyPlugin" /> instance, such as event handlers.
    /// </summary>
    public void Dispose()
    {
        _libraryManager.ItemAdded -= _notificationService.OnItemAdded;
        _libraryManager.ItemUpdated -= _notificationService.OnItemUpdated;
    }
}
