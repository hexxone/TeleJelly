using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Telegram;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TeleJelly;

/// <summary>
///     Helper class for Dependency-injecting the Telegram Background HostedService for ASP.NET
/// </summary>
// ReSharper disable once UnusedType.Global
public class TeleJellyServiceRegistrator : IPluginServiceRegistrator
{
    /// <summary>
    ///     Add custom hosted service for Telegram Bot to DI.
    /// </summary>
    /// <param name="serviceCollection"></param>
    /// <param name="applicationHost"></param>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // listen for commands in the background.
        serviceCollection.AddHostedService<TelegramBackgroundService>();

        // register internal helpers
        serviceCollection.AddSingleton<TelegramBotClientWrapper>();
        serviceCollection.AddSingleton<RequestService>();
        serviceCollection.AddSingleton<NotificationService>();
    }
}
