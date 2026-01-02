using System;
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
    public static async Task<(string title, int? year, bool found)> FindRemoteMetadataAsync(IProviderManager providerManager,
        string imdbId, CancellationToken cancellationToken)
    {
        // 1. Configure Movie Query
        var movieInfo = new MovieInfo { Name = imdbId, ProviderIds = { { nameof(MetadataProvider.Imdb), imdbId } } };
        var movieQuery = new RemoteSearchQuery<MovieInfo> { SearchInfo = movieInfo, IncludeDisabledProviders = false };

        // 2. Configure Series Query
        var seriesInfo = new SeriesInfo { Name = imdbId, ProviderIds = { { nameof(MetadataProvider.Imdb), imdbId } } };
        var seriesQuery = new RemoteSearchQuery<SeriesInfo> { SearchInfo = seriesInfo, IncludeDisabledProviders = false };

        try
        {
            // 3. Run both searches in parallel
            var movieTask = providerManager.GetRemoteSearchResults<Movie, MovieInfo>(movieQuery, cancellationToken);
            var seriesTask = providerManager.GetRemoteSearchResults<Series, SeriesInfo>(seriesQuery, cancellationToken);

            await Task.WhenAll(movieTask, seriesTask).ConfigureAwait(false);

            // 4. Prefer exact IMDb ID entries which have a "year" set;
            // 5. otherwise fall back to the exact ID match, or
            // 6. the first result.
            var allResults = seriesTask.Result.Concat(movieTask.Result).ToArray();
            var firstOrDefault = allResults
                                     .Where(r => r.ProviderIds.Values.Any(id => string.Equals(id, imdbId, StringComparison.OrdinalIgnoreCase)))
                                     .OrderByDescending(r => r.ProductionYear.HasValue)
                                     .FirstOrDefault()
                                 ?? allResults.FirstOrDefault();

            if (firstOrDefault != null)
            {
                return (firstOrDefault.Name, firstOrDefault.ProductionYear, true);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("FindRemoteMetadataAsync Exception: {0}", e);
        }

        return (string.Empty, null, false);
    }
}
