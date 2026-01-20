using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Microsoft.Extensions.Logging;
using TMDbLib.Client;
using TMDbLib.Objects.Find;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

public class MediaAnalyzerService
{
    private readonly ILogger<MediaAnalyzerService> _logger;

    public MediaAnalyzerService(ILogger<MediaAnalyzerService> logger)
    {
        _logger = logger;
    }

    public Task<MediaFileGroup[]> AnalyzeAndGroupFilesAsync(string directoryPath)
    {
        var videoExtensions = new[] { ".mkv", ".mp4", ".avi", ".mov" };
        var subtitleExtensions = new[] { ".srt", ".sub", ".ass" };

        var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

        var analyzedFiles = files.Select(f =>
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            var fileType = AnalyzedFileType.Other;
            if (videoExtensions.Contains(ext))
            {
                fileType = AnalyzedFileType.Video;
            }
            else if (subtitleExtensions.Contains(ext))
            {
                fileType = AnalyzedFileType.Subtitle;
            }

            return new AnalyzedFile { Path = f, SizeBytes = new FileInfo(f).Length, FileType = fileType };
        }).ToList();

        var videoFiles = analyzedFiles.Where(f => f.FileType == AnalyzedFileType.Video).ToList();
        var subtitleFiles = analyzedFiles.Where(f => f.FileType == AnalyzedFileType.Subtitle).ToList();

        var groups = new List<MediaFileGroup>();

        foreach (var video in videoFiles)
        {
            var group = new MediaFileGroup { VideoFile = video };
            var videoBaseName = Path.GetFileNameWithoutExtension(video.Path);

            group.SubtitleFiles.AddRange(
                subtitleFiles.Where(s => Path.GetFileNameWithoutExtension(s.Path).StartsWith(videoBaseName))
            );

            groups.Add(group);
        }

        return Task.FromResult(groups.ToArray());
    }

    public async Task<(string? Title, int? Year, MediaType MediaType)> GetMetadataFromImdbId(string imdbId)
    {
        var apiKey = TeleJellyPlugin.Instance?.Configuration.DownloadManager.TmdbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("TMDb API key is not configured. Cannot fetch metadata.");
            return (null, null, MediaType.Unknown);
        }

        using var tmdbClient = new TMDbClient(apiKey);

        _logger.LogInformation("Fetching metadata for IMDB ID: {ImdbId}", imdbId);
        var result = await tmdbClient.FindAsync(FindExternalSource.Imdb, imdbId);

        if (result.MovieResults.Any())
        {
            var movie = result.MovieResults.First();
            _logger.LogInformation("Found movie: {Title} ({Year})", movie.Title, movie.ReleaseDate?.Year);
            return (movie.Title, movie.ReleaseDate?.Year, MediaType.Movie);
        }

        if (result.TvResults.Any())
        {
            var tvShow = result.TvResults.First();
            _logger.LogInformation("Found series: {Name} ({Year})", tvShow.Name, tvShow.FirstAirDate?.Year);
            return (tvShow.Name, tvShow.FirstAirDate?.Year, MediaType.Series);
        }

        _logger.LogWarning("No movie or series found for IMDB ID: {ImdbId}", imdbId);
        return (null, null, MediaType.Unknown);
    }

    public Task<(int? Season, int? Episode)> ExtractSeasonAndEpisode(string fileName)
    {
        // Regex for S01E01, 1x01, Season 1 Episode 1, etc.
        var regex = new Regex(@"(S|Season)?(\d{1,2})(E|x|Episode)(\d{1,2})", RegexOptions.IgnoreCase);
        var match = regex.Match(fileName);
        if (match.Success)
        {
            var season = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var episode = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
            _logger.LogInformation("Extracted Season {Season}, Episode {Episode} from {FileName}", season, episode, fileName);
            return Task.FromResult(((int?)season, (int?)episode));
        }

        _logger.LogDebug("Could not extract season and episode from {FileName}", fileName);
        return Task.FromResult<(int?, int?)>((null, null));
    }
}
