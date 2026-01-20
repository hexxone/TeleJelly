using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     Result of a media search operation.
/// </summary>
public class MediaSearchResult
{
    /// <summary>
    ///     Gets the search query that was used.
    /// </summary>
    public string QueryText { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the search results.
    /// </summary>
    public List<BaseItem> Items { get; init; } = [];

    /// <summary>
    ///     Gets a value indicating whether there are more results available.
    /// </summary>
    public bool HasMoreResults { get; init; }
}

/// <summary>
///     Service for searching media in Jellyfin libraries.
///     Provides reusable search logic for both commands and inline queries.
/// </summary>
public class MediaSearchService
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MediaSearchService"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    public MediaSearchService(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    ///     Searches for media based on the query and user permissions.
    /// </summary>
    /// <param name="queryText">The search query text.</param>
    /// <param name="allowedLibraryIds">List of library IDs the user has access to. Empty means no access.</param>
    /// <param name="allowAllLibraries">If true, searches all libraries regardless of allowedLibraryIds.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <returns>The search results.</returns>
    public MediaSearchResult Search(
        string queryText,
        List<string> allowedLibraryIds,
        bool allowAllLibraries,
        int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return new MediaSearchResult { QueryText = queryText };
        }

        // Resolve allowed libraries
        var resolvedLibraries = allowAllLibraries
            ? _libraryManager.RootFolder.Children
                .Select(f => f.Id.ToString("N"))
                .ToList()
            : allowedLibraryIds;

        var query = new InternalItemsQuery
        {
            SearchTerm = queryText,
            Recursive = true,
            Limit = maxResults + 1, // fetch one extra to detect "more results"
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            IsVirtualItem = false,
            OrderBy =
            [
                (ItemSortBy.DateLastContentAdded, SortOrder.Descending),
                (ItemSortBy.DateCreated, SortOrder.Descending)
            ]
        };

        if (!allowAllLibraries && resolvedLibraries.Count > 0)
        {
            query.AncestorIds = resolvedLibraries
                .Select(idStr => Guid.TryParse(idStr, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToArray();
        }

        var queryResult = _libraryManager.GetItemsResult(query);
        var results = queryResult.Items.ToList();
        var hasMoreResults = results.Count > maxResults;

        return new MediaSearchResult
        {
            QueryText = queryText,
            Items = results.Take(maxResults).ToList(),
            HasMoreResults = hasMoreResults
        };
    }

    /// <summary>
    ///     Determines the allowed libraries for a user based on their group membership and admin status.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="username">The Telegram username.</param>
    /// <param name="chatId">The chat ID (for group-based searches).</param>
    /// <returns>A tuple containing (allowAllLibraries, allowedLibraryIds).</returns>
    public (bool allowAllLibraries, List<string> allowedLibraryIds) GetUserLibraryAccess(
        PluginConfiguration config,
        string? username,
        long? chatId = null)
    {
        if (string.IsNullOrEmpty(username))
        {
            return (false, []);
        }

        var isAdmin = config.AdminUserNames
            .Any(admin => admin.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (isAdmin)
        {
            return (true, []);
        }

        // Find all groups the user belongs to
        var userGroups = config.TelegramGroups
            .Where(g => g.UserNames.Contains(username, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (userGroups.Count == 0)
        {
            return (false, []);
        }

        // If any group grants access to all folders, allow all
        if (userGroups.Any(g => g.EnableAllFolders))
        {
            return (true, []);
        }

        // Collect all enabled folders from all user's groups
        var allowedFolders = userGroups
            .SelectMany(g => g.EnabledFolders)
            .Distinct()
            .ToList();

        return (false, allowedFolders);
    }

    /// <summary>
    ///     Checks if a user is authorized to use inline queries.
    ///     User must be either an admin or a member of at least one group.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="username">The Telegram username.</param>
    /// <returns>True if the user is authorized.</returns>
    public bool IsUserAuthorizedForInlineQuery(PluginConfiguration config, string? username)
    {
        if (string.IsNullOrEmpty(username))
        {
            return false;
        }

        // Check if admin
        var isAdmin = config.AdminUserNames
            .Any(admin => admin.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (isAdmin)
        {
            return true;
        }

        // Check if member of any group
        return config.TelegramGroups
            .Any(g => g.UserNames.Contains(username, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Generates a Jellyfin URL for viewing an item.
    /// </summary>
    /// <param name="baseUrl">The Jellyfin base URL.</param>
    /// <param name="itemId">The item ID.</param>
    /// <returns>The full URL to view the item.</returns>
    public static string GetJellyfinItemUrl(string? baseUrl, Guid itemId)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }

        return $"{baseUrl.TrimEnd('/')}/web/index.html#!/details?id={itemId:N}";
    }
}
