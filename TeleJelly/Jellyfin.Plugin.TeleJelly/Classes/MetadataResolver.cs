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
    ///     Checks Movies first, then Series.
    /// </summary>
    public static async Task<(string title, int? year, string typeName, bool found)> FindRemoteMetadataAsync(
        IProviderManager providerManager,
        string imdbId,
        CancellationToken cancellationToken)
    {
        // 1. Try Movie
        var movieInfo = new MovieInfo
        {
            Name = imdbId, // Fallback name, providers use IDs primarily
            ProviderIds = { { nameof(MetadataProvider.Imdb), imdbId } }
        };

        var movieQuery = new RemoteSearchQuery<MovieInfo> { SearchInfo = movieInfo, IncludeDisabledProviders = false };

        var movieResults = await providerManager.GetRemoteSearchResults<Movie, MovieInfo>(movieQuery, cancellationToken)
            .ConfigureAwait(false);

        var movie = movieResults.FirstOrDefault();
        if (movie != null)
        {
            return (movie.Name, movie.ProductionYear, "Movie", true);
        }

        // 2. Try Series
        var seriesInfo = new SeriesInfo { Name = imdbId, ProviderIds = { { nameof(MetadataProvider.Imdb), imdbId } } };

        var seriesQuery = new RemoteSearchQuery<SeriesInfo> { SearchInfo = seriesInfo, IncludeDisabledProviders = false };

        var seriesResults = await providerManager.GetRemoteSearchResults<Series, SeriesInfo>(seriesQuery, cancellationToken)
            .ConfigureAwait(false);

        var series = seriesResults.FirstOrDefault();
        if (series != null)
        {
            return (series.Name, series.ProductionYear, "Series", true);
        }

        return (string.Empty, null, string.Empty, false);
    }
}
