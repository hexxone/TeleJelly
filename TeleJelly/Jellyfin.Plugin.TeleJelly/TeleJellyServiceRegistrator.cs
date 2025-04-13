using Jellyfin.Plugin.TeleJelly.Telegram;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TeleJelly;

/// <summary>
///
/// </summary>
public class TeleJellyServiceRegistrator : IPluginServiceRegistrator
{
    /// <summary>
    ///     Add custom hosted service for Telegram Bot.
    /// </summary>
    /// <param name="serviceCollection"></param>
    /// <param name="applicationHost"></param>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Register background service
        serviceCollection.AddHostedService<TelegramBackgroundService>();
    }
}
