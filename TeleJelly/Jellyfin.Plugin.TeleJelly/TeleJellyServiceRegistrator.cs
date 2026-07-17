using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Jellyfin.Plugin.TeleJelly.Services.Download.Health;
using Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search.Providers.Catalog;
using Jellyfin.Plugin.TeleJelly.Services.Download.Torrents;
using Jellyfin.Plugin.TeleJelly.Services.Logging;
using Jellyfin.Plugin.TeleJelly.Telegram;
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
        // register internal helpers
        serviceCollection.AddSingleton<TelegramBotClientWrapper>();
        serviceCollection.AddSingleton<ICommandProvider, DefaultCommandProvider>();
        serviceCollection.AddSingleton<IRequestService, RequestService>();
        serviceCollection.AddSingleton<INotificationService, NotificationService>();

        // Download manager services
        serviceCollection.AddSingleton<DownloadManagerLogStore>();
        serviceCollection.AddSingleton<IDownloadManagerLogStore>(serviceProvider => serviceProvider.GetRequiredService<DownloadManagerLogStore>());
        serviceCollection.AddSingleton<IDownloadManagerLogWriter>(serviceProvider => serviceProvider.GetRequiredService<DownloadManagerLogStore>());
        serviceCollection.AddSingleton<ILoggerProvider, TeleJellyLoggerProvider>();
        serviceCollection.AddSingleton<IDownloadOrchestrator, DownloadOrchestrator>();
        serviceCollection.AddSingleton<ArchiveExtractionService>();
        serviceCollection.AddSingleton<MediaAnalyzerService>();
        serviceCollection.AddSingleton<PathTemplateService>();
        serviceCollection.AddSingleton<MediaFileOrganizerService>();
        serviceCollection.AddSingleton<IServiceHealthMonitor, ServiceHealthMonitor>();
        serviceCollection.AddSingleton<QualityRuleEngine>();
        serviceCollection.AddSingleton<ISearchDocumentFetcher, HttpClientSearchDocumentFetcher>();
        serviceCollection.AddSingleton<IDownloadLinkValidator, FileCryptContainerValidator>();
        serviceCollection.AddSingleton<SearchOrchestrator>();

        // Search providers
        serviceCollection.AddSingleton<ISearchProvider, FunxdSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, JjsSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, HdSourceSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, FilmfansSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, SerienfansSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, CrawliSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, DataLoadSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, DdlWarezSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, Nima4kSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, MovieblogSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, DiscoLoadSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, HdencodeSearchProvider>();
        serviceCollection.AddSingleton<ISearchProvider, ByteToSearchProvider>();

        // Register download clients (torrent services)
        serviceCollection.AddSingleton<ITorrentDownloadService, TransmissionService>();
        serviceCollection.AddSingleton<ITorrentDownloadService, QBittorrentService>();

        // Register download clients (hosted services)
        serviceCollection.AddSingleton<IHostedDownloadService, JDownloader2Service>();
        serviceCollection.AddSingleton<IHostedDownloadService, PyLoadService>();

        // listen for commands in the background.
        serviceCollection.AddHostedService<TelegramBackgroundService>();
        serviceCollection.AddHostedService<DownloadManagerService>();
    }
}
