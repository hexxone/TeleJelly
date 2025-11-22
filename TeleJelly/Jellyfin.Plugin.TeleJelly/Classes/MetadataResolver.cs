using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.TeleJelly.Classes;

/// <summary>
///     Provides functionality to resolve metadata information through Jellyfin's remote providers.
/// </summary>
public static class MetadataResolver
{
    /// <summary>
    ///     Attempts to find metadata for an IMDb ID by querying Jellyfin's remote providers.
    ///     Queries both types in parallel and prioritizes Series on collision to fix common provider misclassification.
    /// </summary>
    public static async Task<(string title, int? year, string typeName, bool found)> FindRemoteMetadataAsync(
        IProviderManager providerManager,
        string imdbId,
        CancellationToken cancellationToken)
    {
        // 1. Configure Movie Query
        var movieInfo = new MovieInfo
        {
            Name = imdbId,
            ProviderIds = { { nameof(MetadataProvider.Imdb), imdbId } }
        };
        var movieQuery = new RemoteSearchQuery<MovieInfo> { SearchInfo = movieInfo, IncludeDisabledProviders = false };

        // 2. Configure Series Query
        var seriesInfo = new SeriesInfo
        {
            Name = imdbId,
            ProviderIds = { { nameof(MetadataProvider.Imdb), imdbId } }
        };
        var seriesQuery = new RemoteSearchQuery<SeriesInfo> { SearchInfo = seriesInfo, IncludeDisabledProviders = false };

        // 3. Run both searches in parallel
        var movieTask = providerManager.GetRemoteSearchResults<Movie, MovieInfo>(movieQuery, cancellationToken);
        var seriesTask = providerManager.GetRemoteSearchResults<Series, SeriesInfo>(seriesQuery, cancellationToken);

        await Task.WhenAll(movieTask, seriesTask).ConfigureAwait(false);

        var movie = movieTask.Result.FirstOrDefault();
        var series = seriesTask.Result.FirstOrDefault();

        // 4. Logic: Prioritize Series
        // If we found a Series, we return it immediately.
        // REASON: It is very common for Movie providers (like OMDb) to return a Series item for a given ID.
        // It is rare for Series providers to return a Movie item.
        // Therefore, if 'series' is not null, it is almost certainly a Series.
        if (series != null)
        {
            return (series.Name, series.ProductionYear, "Series", true);
        }

        if (movie != null)
        {
            return (movie.Name, movie.ProductionYear, "Movie", true);
        }

        return (string.Empty, null, string.Empty, false);
    }
}
