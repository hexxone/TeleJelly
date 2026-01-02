using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes;
using Jellyfin.Plugin.TeleJelly.Services;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Command "/request {imdb_id}" which searches for the title / series and puts it on a persistent "request" list.
///     It includes the User who sent the Request and the Date.
///     If the entry is already contained in the list, it is not added again.
///     There is a limit to the number of requests per user (currently maximum 5).
///     If there is no argument given, it prints the existing list of requests, in a simplified format like "search",
///     but only Name, Year, Type, Extra Info + ImDb inline link. No images.
/// </summary>
// ReSharper disable once UnusedType.Global
internal class CommandRequest : ICommandBase
{
    private const int MaxRequestsPerUser = 5;

    /// <inheritdoc />
    public string Command => "request";

    /// <inheritdoc />
    public bool NeedsAdmin => false;

    /// <inheritdoc />
    public async Task Execute(
        ITelegramBotService telegramBotService,
        Message message,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var botClient = telegramBotService.BotClientWrapper.Client;
        if (botClient == null)
        {
            telegramBotService.Logger.LogError("Telegram Bot Client wrapper is null in CommandRequest.");
            return;
        }

        var group = telegramBotService.Config.TelegramGroups
            .FirstOrDefault(g => g.TelegramGroupChat?.TelegramChatId == message.Chat.Id);

        if (!await EnsureUserAllowedAsync(botClient, group, isAdmin, message, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var requestService = telegramBotService.ServiceProvider.GetRequiredService<RequestService>();

        if (!TryExtractImdbId(message.Text, out var imdbId))
        {
            await HandleListRequestAsync(botClient, message.Chat.Id, group, requestService, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await HandleAddRequestAsync(telegramBotService, botClient, message, imdbId, requestService, cancellationToken)
            .ConfigureAwait(false);
    }

    // messageText can be:
    // "/request"
    // "/request@BotName"
    // "/request tt1234567"
    // "/request@BotName tt1234567"
    // "/request https://www.imdb.com/title/tt1234567/"
    // "/request@BotName https://www.imdb.com/title/tt1234567/"
    // Extracts IMDb ID from URL patterns like:
    // https://www.imdb.com/title/tt1234567/
    // https://www.imdb.com/title/tt1234567
    // https://imdb.com/title/tt1234567/
    private static bool TryExtractImdbId(string? messageText, out string imdbId)
    {
        imdbId = string.Empty;
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return false;
        }

        var parts = messageText.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var argument = parts[1].Trim();

        // check if starts with tt, otherwise only return if it's really a valid URL.
        if (argument.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            imdbId = argument;
            return true;
        }

        if (!Uri.TryCreate(argument, UriKind.Absolute, out var uri)
            || !uri.Host.EndsWith("imdb.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("title", StringComparison.OrdinalIgnoreCase) &&
                segments[i + 1].StartsWith("tt", StringComparison.OrdinalIgnoreCase))
            {
                imdbId = segments[i + 1];
                return true;
            }
        }

        return false;
    }

    private static async Task<string?> BuildRequestListMessageAsync(TelegramGroup? group, RequestService requestService, CancellationToken cancellationToken)
    {
        var snapshot = await requestService.GetRequestsAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine(TelegramMarkdown.Escape("📋 Current Requests 📋"));
        sb.AppendLine();

        var index = 1;
        foreach (var mediaRequest in snapshot.OrderBy(r => r.RequestedAtUtc))
        {
            if (group != null)
            {
                // Don't display the request if the user is not part of the current group
                // Check if request owner (@username) is in the allowed group list
                var requestOwner = mediaRequest.UserDisplayName.TrimStart('@');
                if (!group.UserNames.Contains(requestOwner, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var indexPrefix = $"{index++}. ";
            sb.Append(TelegramMarkdown.Escape(indexPrefix));

            AppendMediaRequestInfo(sb, mediaRequest);
        }

        sb.AppendLine();
        sb.Append("Use\\: `/request <imdb_id_or_url>` to add more\\.");
        return sb.ToString();
    }

    private static void AppendMediaRequestInfo(StringBuilder sb, MediaRequest mediaRequest)
    {
        // Title with IMDB Url
        sb.Append('[');
        sb.Append(TelegramMarkdown.Escape(mediaRequest.Title));
        sb.Append("](");
        sb.Append(TelegramMarkdown.Escape($"https://www.imdb.com/title/{mediaRequest.ImdbId}/"));
        sb.Append(')');

        if (mediaRequest.Year.HasValue)
        {
            sb.Append(TelegramMarkdown.Escape($" ({mediaRequest.Year.Value})"));
        }

        // Put `@user` in code-block so that doesnt get notified everytime.
        sb.Append(TelegramMarkdown.Escape(" by: "))
            .Append('`')
            .Append(TelegramMarkdown.Escape(mediaRequest.UserDisplayName))
            .Append('`')
            .AppendLine();
    }

    private static string GetUserDisplayName(User? user)
    {
        if (user == null)
        {
            return "Unknown";
        }

        if (!string.IsNullOrWhiteSpace(user.Username))
        {
            return "@" + user.Username;
        }

        var name = user.FirstName.Trim();

        if (!string.IsNullOrWhiteSpace(user.LastName))
        {
            if (name.Length > 0)
            {
                name += " ";
            }

            name += user.LastName.Trim();
        }

        return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
    }

    private static async Task<bool> EnsureUserAllowedAsync(
        ITelegramBotClient botClient,
        TelegramGroup? group,
        bool isAdmin,
        Message message,
        CancellationToken cancellationToken)
    {
        if (isAdmin)
        {
            return true;
        }

        if (group != null)
        {
            // If we are in a known group, check if user is allowed.
            var username = message.From?.Username;
            if (string.IsNullOrEmpty(username) ||
                !group.UserNames.Contains(username, StringComparer.OrdinalIgnoreCase))
            {
                await botClient.SendMessage(
                    message.Chat.Id,
                    "You cannot make requests in this group yet.",
                    cancellationToken: cancellationToken);
                return false;
            }

            return true;
        }

        // Group isn't linked yet
        if (message.Chat.Type != ChatType.Private)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                Constants.GroupWelcomeMessage,
                cancellationToken: cancellationToken);
            return false;
        }

        await botClient.SendMessage(
            message.Chat.Id,
            Constants.PrivateUserWelcomeMessage,
            cancellationToken: cancellationToken);
        return false;
    }

    private static Task SendMarkdownAsync(
        ITelegramBotClient client,
        ChatId chatId,
        string text,
        CancellationToken cancellationToken)
    {
        return client.SendMessage(
            chatId,
            text,
            ParseMode.MarkdownV2,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            cancellationToken: cancellationToken);
    }

    private static async Task HandleListRequestAsync(
        ITelegramBotClient botClient,
        ChatId chatId,
        TelegramGroup? group,
        RequestService requestService,
        CancellationToken cancellationToken)
    {
        var listText = await BuildRequestListMessageAsync(group, requestService, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(listText))
        {
            await botClient.SendMessage(
                chatId,
                "No requests yet. Use: /request <imdb_id>",
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: cancellationToken);
            return;
        }

        await SendMarkdownAsync(botClient, chatId, listText, cancellationToken).ConfigureAwait(false);
    }

    private static async Task HandleAddRequestAsync(
        ITelegramBotService telegramBotService,
        ITelegramBotClient botClient,
        Message message,
        string imdbId,
        RequestService requestService,
        CancellationToken cancellationToken)
    {
        var userId = message.From?.Id.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        var userDisplayName = GetUserDisplayName(message.From);

        var providerManager = telegramBotService.ServiceProvider.GetRequiredService<IProviderManager>();

        var (title, year, found) = await MetadataResolver
            .FindRemoteMetadataAsync(providerManager, imdbId, cancellationToken)
            .ConfigureAwait(false);

        if (!found)
        {
            var notFound = $"Could not find any movie or series metadata for IMDb id \"{TelegramMarkdown.Escape(imdbId)}\".";
            await SendMarkdownAsync(botClient, message.Chat.Id, notFound, cancellationToken).ConfigureAwait(false);
            return;
        }

        var request = new MediaRequest
        {
            ItemId = Guid.Empty,
            ImdbId = imdbId,
            Title = title,
            Year = year,
            UserId = userId,
            UserDisplayName = userDisplayName,
            RequestedAtUtc = DateTime.UtcNow
        };

        var result = await requestService
            .TryAddRequestAsync(request, MaxRequestsPerUser, cancellationToken)
            .ConfigureAwait(false);

        switch (result)
        {
            case RequestAddResult.UserLimitReached:
            {
                var msg = $"You have reached the maximum of {MaxRequestsPerUser} requests.";
                await SendMarkdownAsync(botClient, message.Chat.Id, TelegramMarkdown.Escape(msg), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            case RequestAddResult.Removed:
            {
                var msg = $"Request for \"{title}\" removed.";
                await SendMarkdownAsync(botClient, message.Chat.Id, TelegramMarkdown.Escape(msg), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            case RequestAddResult.Duplicate:
            {
                var duplicateMsg = BuildDuplicateMessage(title, imdbId, year);
                await SendMarkdownAsync(botClient, message.Chat.Id, duplicateMsg, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            case RequestAddResult.Added:
            {
                var addedMsg = BuildAddedMessage(request);
                await SendMarkdownAsync(botClient, message.Chat.Id, addedMsg, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            default:
            {
                const string msg = "An error occurred while adding the request.";
                await SendMarkdownAsync(botClient, message.Chat.Id, TelegramMarkdown.Escape(msg), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
        }
    }

    private static string BuildDuplicateMessage(string title, string imdbId, int? year)
    {
        var sb = new StringBuilder();
        sb.Append("The title ");
        sb.Append('[').Append(TelegramMarkdown.Escape(title)).Append("](");
        sb.Append(TelegramMarkdown.Escape($"https://www.imdb.com/title/{imdbId}/")).Append(')');

        if (year.HasValue)
        {
            sb.Append(TelegramMarkdown.Escape($" ({year})"));
        }

        sb.Append(" was already requested by another user\\.");
        return sb.ToString();
    }

    private static string BuildAddedMessage(MediaRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(TelegramMarkdown.Escape("📋 Request added ✅"));
        sb.Append("\\- ");
        AppendMediaRequestInfo(sb, request);
        return sb.ToString();
    }
}
