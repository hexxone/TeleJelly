using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TeleJelly.Classes;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task Execute(TelegramBotService telegramBotService, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var botClient = telegramBotService._client;
        if (message.Chat.Type == ChatType.Private && !isAdmin)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                Constants.PrivateUserWelcomeMessage,
                cancellationToken: cancellationToken);

            return;
        }

        var group = telegramBotService._config.TelegramGroups.FirstOrDefault(g => g.TelegramGroupChat?.TelegramChatId == message.Chat.Id);
        if (message.Chat.Type != ChatType.Private && group == null)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                Constants.GroupWelcomeMessage,
                cancellationToken: cancellationToken);

            return;
        }

        var libraryManager = telegramBotService._serviceProvider.GetRequiredService<ILibraryManager>();

        var allowAllLibraries = group?.EnableAllFolders ?? isAdmin;


        var allowedLibraries = allowAllLibraries
            ? libraryManager.RootFolder.Children
                .Select(f => f.Id.ToString("N"))
                .ToList()
            : group?.EnabledFolders
              ?? [];

        // get search params and search for them ignoring casing
        var queryText = GetSearchQuery(message.Text);
        if (string.IsNullOrWhiteSpace(queryText))
        {
            // Send usage as plain text to avoid Markdown headaches
            await botClient.SendMessage(
                message.Chat.Id,
                "Usage: /search <text> – please provide a search term.",
                ParseMode.None,
                cancellationToken: cancellationToken);

            return;
        }

        var query = new InternalItemsQuery
        {
            SearchTerm = queryText,
            Recursive = true,
            Limit = MaxResultCount + 1, // fetch one extra to detect "more results"
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            OrderBy =
            [
                (ItemSortBy.DateLastContentAdded, SortOrder.Descending),
                (ItemSortBy.DateCreated, SortOrder.Descending)
            ]
        };

        if (!allowAllLibraries && allowedLibraries.Count > 0)
        {
            query.AncestorIds = allowedLibraries
                .Select(idStr => Guid.TryParse(idStr, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToArray();
        }

        var queryResult = libraryManager.GetItemsResult(query);
        var results = queryResult.Items.ToList();

        if (results.Count == 0)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                $"No results found for \"{queryText}\".",
                ParseMode.None,
                cancellationToken: cancellationToken);
            return;
        }

        // get results in a fancy list with details and imdb link
        var resultsToShow = results.Take(MaxResultCount).ToList();
        var hasMore = results.Count > MaxResultCount;

        var sb = new StringBuilder();

        var safeQueryText = TelegramMarkdown.Escape(queryText);
        sb.Append(TelegramMarkdown.Escape("Search results for \""));
        sb.Append(safeQueryText);
        sb.AppendLine(TelegramMarkdown.Escape("\":"));

        var baseUrl = telegramBotService._config.LoginBaseUrl;

        var index = 1;
        foreach (var item in resultsToShow)
        {
            // Number prefix (e.g. "1. ") must be escaped for MarkdownV2
            var indexPrefix = $"{index++}. ";
            sb.Append(TelegramMarkdown.Escape(indexPrefix));

            // Build the display text (name + year + type)
            var displayText = item.GetDisplayText();
            var safeDisplayText = TelegramMarkdown.Escape(displayText);

            // Make it a link if baseUrl is available
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                var itemUrl = $"{baseUrl.TrimEnd('/')}/web/index.html#!/details?id={item.Id:N}";
                var safeItemUrl = TelegramMarkdown.Escape(itemUrl);

                sb.Append('[')
                    .Append(safeDisplayText)
                    .Append("](")
                    .Append(safeItemUrl)
                    .Append(')');
            }
            else
            {
                sb.Append(safeDisplayText);
            }

            var extraLink = item.GetExtraLink();
            if (extraLink != null)
            {
                sb.Append(extraLink);
            }

            sb.AppendLine();

            var mediaStreams = item.GetMediaStreams();
            if (mediaStreams is { Count: > 0 })
            {
                var audioLanguages = mediaStreams
                    .Where(s => s.Type == MediaStreamType.Audio && !string.IsNullOrEmpty(s.Language))
                    .Select(s => s.Language)
                    .Distinct()
                    .ToArray();

                var subtitleLanguages = mediaStreams
                    .Where(s => s.Type == MediaStreamType.Subtitle && !string.IsNullOrEmpty(s.Language))
                    .Select(s => s.Language)
                    .Distinct()
                    .ToArray();

                if (audioLanguages.Length > 0)
                {
                    var audioPrefix = TelegramMarkdown.Escape("   Audio: ");
                    sb.Append(audioPrefix);
                    sb.AppendLine(string.Join(", ", audioLanguages.Select(TelegramMarkdown.Escape)));
                }

                if (subtitleLanguages.Length > 0)
                {
                    var subsPrefix = TelegramMarkdown.Escape("   Subtitles: ");
                    sb.Append(subsPrefix);
                    sb.AppendLine(string.Join(", ", subtitleLanguages.Select(TelegramMarkdown.Escape)));
                }
            }

            // Add a blank line between entries
            sb.AppendLine();
        }

        // think about pagination ? for now only show the first 5 and hint
        if (hasMore)
        {
            sb.AppendLine(TelegramMarkdown.Escape("Only showing first 5 results. Refine your search to narrow down further."));
        }

        await botClient.SendMessage(
            message.Chat.Id,
            sb.ToString(),
            ParseMode.MarkdownV2,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            cancellationToken: cancellationToken);
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
}
