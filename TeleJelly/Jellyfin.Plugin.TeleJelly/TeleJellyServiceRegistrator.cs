using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Telegram;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        // Register background service
        serviceCollection.AddHostedService<TelegramBackgroundService>();
        serviceCollection.AddSingleton<TelegramBotClientWrapper>();
        serviceCollection.AddSingleton<NotificationService>(s =>
            new NotificationService(
                s.GetRequiredService<ILogger<NotificationService>>(),
                s.GetRequiredService<TelegramBotClientWrapper>(),
                s.GetRequiredService<IConfigurationManager>(),
                s
            ));
    }
}
