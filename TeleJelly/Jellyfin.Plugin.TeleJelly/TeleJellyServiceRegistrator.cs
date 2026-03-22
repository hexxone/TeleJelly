using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Services.Download;
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
        // register internal helpers
        serviceCollection.AddSingleton<TelegramBotClientWrapper>();
        serviceCollection.AddSingleton<ICommandProvider, DefaultCommandProvider>();
        serviceCollection.AddSingleton<IRequestService, RequestService>();
        serviceCollection.AddSingleton<INotificationService, NotificationService>();

        // Download manager services
        serviceCollection.AddSingleton<IDownloadOrchestrator, DownloadOrchestrator>();
        serviceCollection.AddSingleton<ArchiveExtractionService>();
        serviceCollection.AddSingleton<MediaAnalyzerService>();
        serviceCollection.AddSingleton<PathTemplateService>();
        serviceCollection.AddSingleton<MediaFileOrganizerService>();
        serviceCollection.AddSingleton<IServiceHealthMonitor, ServiceHealthMonitor>();

        // Register download clients (torrent services)
        serviceCollection.AddScoped<ITorrentDownloadService, TransmissionService>();
        serviceCollection.AddScoped<ITorrentDownloadService, QBittorrentService>();

        // Register download clients (hosted services)
        serviceCollection.AddScoped<IHostedDownloadService, JDownloader2Service>();
        serviceCollection.AddScoped<IHostedDownloadService, PyLoadService>();

        // listen for commands in the background.
        serviceCollection.AddHostedService<TelegramBackgroundService>();
        serviceCollection.AddHostedService<DownloadManagerService>();
    }
}
