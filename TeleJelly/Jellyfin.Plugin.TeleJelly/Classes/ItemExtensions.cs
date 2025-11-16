using System;
using System.Linq;
using System.Text;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.TeleJelly.Classes;

internal static class ItemExtensions
{

    internal static string GetDisplayText(this BaseItem item)
    {
        var displayText = item.Name;
        if (item.ProductionYear.HasValue)
        {
            displayText += $" ({item.ProductionYear.Value})";
        }

        // Add media type
        if (item is Movie movie)
        {
            displayText += " [Movie";
            var minuteDuration = movie.RunTimeTicks.HasValue ? (int)(movie.RunTimeTicks.Value / TimeSpan.TicksPerMinute) : 0;
            if (minuteDuration > 0)
            {
                displayText += $", {minuteDuration}min";
            }
            displayText += "]";
        }
        else if (item is Series series)
        {
            var episodeCount = series.GetRecursiveChildren().OfType<Episode>().Count();
            var seasonCount = series.GetRecursiveChildren().OfType<Season>().Count();
            displayText += $" [Series, {seasonCount} seasons, {episodeCount} episodes]";
        }
        else if (item is Season season)
        {
            var episodeCount = season.GetRecursiveChildren().OfType<Episode>().Count();
            displayText += $" [Season {season.IndexNumber ?? 0}, {episodeCount} episodes]";
        }
        else if (item is Episode episode)
        {
            displayText += $" [Season {episode.ParentIndexNumber ?? 0}, episode {episode.IndexNumber ?? 0}]";
        }

        return displayText;
    }


    internal static string? GetExtraLink(this BaseItem item)
    {
        var imdbId = item.GetProviderId(MetadataProvider.Imdb);
        if (!string.IsNullOrEmpty(imdbId))
        {
            return $" - [IMDb](https://www.imdb.com/title/{imdbId})";
        }

        var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
        if (!string.IsNullOrEmpty(tmdbId))
        {
            var tmdbUrl = item is Movie
                ? $"https://www.themoviedb.org/movie/{tmdbId}"
                : $"https://www.themoviedb.org/tv/{tmdbId}";
            return $" - [TMDb]({tmdbUrl})";
        }

        var tvdbId = item.GetProviderId(MetadataProvider.Tvdb);
        if (!string.IsNullOrEmpty(tvdbId))
        {
            return $" - [TVDb](https://www.thetvdb.com/?tab=series&id={tvdbId})";
        }

        // see -> https://github.com/ryandash/jellyfin-plugin-myanimelist/blob/84af6e6720babaedd78d273ca41ab4b9f5bb4148/Jellyfin.Plugin.MyAnimeList/Providers/ProviderNames.cs
        var malId = item.GetProviderId("MyAnimeList");
        if (!string.IsNullOrEmpty(malId))
        {
            return $" - [MyAnimeList](https://myanimelist.net/anime/{malId})";
        }

        // see -> https://github.com/jellyfin/jellyfin-plugin-anidb/blob/e521ed7f58eca2ee6940e94b1db9000c146d8666/Jellyfin.Plugin.AniDB/Providers/ProviderNames.cs
        var aniDbId = item.GetProviderId("AniDB");
        if (!string.IsNullOrEmpty(aniDbId))
        {
            return $" - [AniDB](https://anidb.net/anime/{aniDbId})";
        }

        // see -> https://github.com/jellyfin/jellyfin-plugin-anilist/blob/16f4594e27f6d711218e54205ce42cc4ff0ab2ee/Jellyfin.Plugin.AniList/Providers/ProviderNames.cs
        var aniListId = item.GetProviderId("AniList");
        if (!string.IsNullOrEmpty(aniListId))
        {
            return $" - [AniList](https://anilist.co/anime/{aniListId})";
        }

        return null;
    }
}
