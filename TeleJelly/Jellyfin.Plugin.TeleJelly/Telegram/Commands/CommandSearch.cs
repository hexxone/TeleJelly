using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Command for searching media on the Server.
/// </summary>
// ReSharper disable once UnusedType.Global
internal class CommandSearch : ICommandBase
{
    private const int MaxResultCount = 5;


    /// <summary>
    ///     Gets what command to trigger on.
    /// </summary>
    public string Command => "search";

    /// <summary>
    ///     Gets a value indicating whether this command can only be run as Admin.
    /// </summary>
    public bool NeedsAdmin => false;

    /// <summary>
    ///     The action code to trigger for the Command.
    /// </summary>
    public async Task Execute(ITelegramBotService telegramBotService, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var botClient = telegramBotService.BotClientWrapper.Client;
        if (botClient == null)
        {
            telegramBotService.Logger.LogError("Telegram Bot Client wrapper is null in CommandSearch.");
            return;
        }

        if (message.Chat.Type == ChatType.Private && !isAdmin)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                Constants.PrivateUserWelcomeMessage,
                cancellationToken: cancellationToken);

            return;
        }

        var group = telegramBotService.Config.TelegramGroups.FirstOrDefault(g => g.TelegramGroupChat?.TelegramChatId == message.Chat.Id);
        if (message.Chat.Type != ChatType.Private && group == null)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                Constants.GroupWelcomeMessage,
                cancellationToken: cancellationToken);

            return;
        }

        var (queryText, results) = await GetSearchResults(telegramBotService, message, isAdmin, cancellationToken, group, botClient);

        if (results is not { Count: > 0 })
        {
            await botClient.SendMessage(
                message.Chat.Id,
                $"No results found for \"{queryText}\".",
                cancellationToken: cancellationToken);
            return;
        }

        // get results in a fancy list with details and imdb link
        var sb = new StringBuilder();
        sb.Append(TelegramMarkdown.Escape("Search results for \""));
        sb.Append(TelegramMarkdown.Escape(queryText));
        sb.AppendLine(TelegramMarkdown.Escape("\":"));

        var baseUrl = telegramBotService.Config.LoginBaseUrl;

        var index = 1;
        foreach (var item in results.Take(MaxResultCount))
        {
            AppendSearchResultInfos(index++, sb, item, baseUrl);
        }

        // think about pagination in a group-chat ? for now only show the first 5 and hint

        await botClient.SendMessage(
            message.Chat.Id,
            sb.ToString(),
            ParseMode.MarkdownV2,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            cancellationToken: cancellationToken);
    }

    private static async Task<(string queryText, List<BaseItem>? results)> GetSearchResults(ITelegramBotService telegramBotService, Message message, bool isAdmin,
        CancellationToken cancellationToken, TelegramGroup? group, ITelegramBotClient botClient)
    {
        var libraryManager = telegramBotService.ServiceProvider.GetRequiredService<ILibraryManager>();
        var searchService = new MediaSearchService(libraryManager);

        // get search params and search for them ignoring casing
        var queryText = GetSearchQuery(message.Text);
        if (string.IsNullOrWhiteSpace(queryText))
        {
            // Send usage as plain text to avoid Markdown headaches
            await botClient.SendMessage(
                message.Chat.Id,
                "Usage: /search <text> – please provide a search term.",
                cancellationToken: cancellationToken);

            return (queryText, null);
        }

        // Determine library access
        var allowAllLibraries = group?.EnableAllFolders ?? isAdmin;
        var allowedLibraries = group?.EnabledFolders ?? [];

        // Use the shared search service
        var searchResult = searchService.Search(queryText, allowedLibraries, allowAllLibraries, MaxResultCount);

        return (queryText, searchResult.Items);
    }

    private static void AppendSearchResultInfos(int index, StringBuilder sb, BaseItem item, string? baseUrl)
    {
        sb.Append(TelegramMarkdown.Escape($"{index}. "));

        sb.Append(item.GetTelegramHyperlink(baseUrl));

        var extraLink = item.GetExtraLink();
        if (extraLink != null)
        {
            sb.Append(extraLink);
        }

        sb.AppendLine();

        // Video
        var videoStream = item.GetMediaStreams().FirstOrDefault(s => s.Type == MediaStreamType.Video);
        if (videoStream != null)
        {
            AppendVideoStreamInfo(videoStream, item, sb);
        }

        // Audio
        var audioStreams = item.GetMediaStreams().Where(s => s.Type == MediaStreamType.Audio).ToList();
        if (audioStreams.Count > 0)
        {
            AppendAudioStreamInfo(sb, audioStreams);
        }

        // Subtitles
        var subtitleLanguages = item.GetStreamLanguages(MediaStreamType.Subtitle);
        if (subtitleLanguages.Length > 0)
        {
            var subsPrefix = TelegramMarkdown.Escape("   Subtitles: ");
            sb.Append(subsPrefix);
            sb.AppendLine(string.Join(", ", subtitleLanguages.Select(TelegramMarkdown.Escape)));
        }

        // Add a blank line between entries
        sb.AppendLine();
    }


    private static string GetSearchQuery(string? messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return string.Empty;
        }

        // messageText is like: "/search something to find"
        // or "/search@BotName something to find"
        var parts = messageText.Trim().Split(' ', 2);
        if (parts.Length < 2)
        {
            return string.Empty;
        }

        return parts[1].Trim();
    }


    #region Audio and Video

    private static void AppendVideoStreamInfo(MediaStream videoStream, BaseItem item, StringBuilder sb)
    {
        var resolution = ExtractVideoResolutionInfo(videoStream);

        var bitrate = CalculateBitrate(videoStream, item);

        var videoInfo = resolution;
        if (bitrate > 0)
        {
            var bitrateMbps = Math.Round(bitrate / 1_000_000.0, 1);
            var formatted = string.Create(CultureInfo.InvariantCulture, $"({bitrateMbps} Mbps)");
            if (!string.IsNullOrEmpty(videoInfo))
            {
                videoInfo += $" ({formatted})";
            }
            else
            {
                videoInfo = formatted;
            }
        }

        if (string.IsNullOrEmpty(videoInfo))
        {
            return;
        }

        sb.Append(TelegramMarkdown.Escape("   Video: "));
        sb.AppendLine(TelegramMarkdown.Escape(videoInfo));
    }


    private static void AppendAudioStreamInfo(StringBuilder sb, List<MediaStream> audioStreams)
    {
        var audioPrefix = TelegramMarkdown.Escape("   Audio: ");
        sb.Append(audioPrefix);

        var audioInfos = audioStreams.Select(s =>
        {
            var lang = !string.IsNullOrEmpty(s.Language) && !s.Language.Equals("und", StringComparison.OrdinalIgnoreCase)
                ? s.Language
                : "Unknown";

            var details = new StringBuilder();
            if (!string.IsNullOrEmpty(s.Codec))
            {
                details.Append(s.Codec.ToUpperInvariant());
            }

            if (!string.IsNullOrEmpty(s.ChannelLayout))
            {
                if (details.Length > 0)
                {
                    details.Append(' ');
                }

                details.Append(s.ChannelLayout);
            }
            else if (s.Channels.HasValue)
            {
                if (details.Length > 0)
                {
                    details.Append(' ');
                }

                details.Append(CultureInfo.InvariantCulture, $"{s.Channels}ch");
            }

            if (s.BitRate.HasValue)
            {
                if (details.Length > 0)
                {
                    details.Append(' ');
                }

                details.Append(CultureInfo.InvariantCulture, $"{Math.Round(s.BitRate.Value / 1000.0)}kbps");
            }

            return $"{lang} ({details})";
        });

        sb.AppendLine(string.Join(", ", audioInfos.Select(TelegramMarkdown.Escape)));
    }

    #endregion


    #region Stream-Utils

    private static long CalculateBitrate(MediaStream videoStream, BaseItem item)
    {
        long streamBitRate = videoStream.BitRate ?? 0;
        if (streamBitRate != 0 || item.RunTimeTicks is not > 0)
        {
            return streamBitRate;
        }

        // Estimate bitrate from size / duration
        // Try to get size from sources
        var totalSize = item.GetMediaSources(false).Sum(s => s.Size) ?? 0;
        if (totalSize <= 0)
        {
            return 0;
        }

        var durationSeconds = item.RunTimeTicks.Value / 10_000_000.0;
        return (long)(totalSize * 8 / durationSeconds);
    }

    private static string? ExtractVideoResolutionInfo(MediaStream videoStream)
    {
        // Try to get a display title like "1080p" if available and looks like a standard resolution
        if (!string.IsNullOrEmpty(videoStream.DisplayTitle))
        {
            // Regex to match standard resolutions like 480p, 720p, 1080p, 4K, 2160p (case-insensitive)
            var matches = Regex.Matches(videoStream.DisplayTitle, @"(?i)(\d{3,4}p|4k|8k|sd|hd)");
            if (matches.Count > 0)
            {
                return matches[0].Groups[1].Value;
            }
        }

        // Fallback to Width x Height if no valid DisplayTitle
        if (videoStream is not { Height: > 0 })
        {
            return null;
        }

        if (videoStream is { Width: > 0 })
        {
            var (x, y) = GetAspectRatio(videoStream.Width.Value, videoStream.Height.Value);
            if (x != 0 && y != 0)
            {
                return $"{videoStream.Height}p ({x}:{y})";
            }

            return $"{videoStream.Width}x{videoStream.Height}p";
        }

        return $"{videoStream.Height}p";
    }

    private static (int arWidth, int arHeight) GetAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        var gcd = Gcd(width, height);
        return (width / gcd, height / gcd);
    }

    private static int Gcd(int a, int b)
    {
        // Euclidean algorithm
        while (b != 0)
        {
            var temp = b;
            b = a % b;
            a = temp;
        }

        return a;
    }

    #endregion
}
