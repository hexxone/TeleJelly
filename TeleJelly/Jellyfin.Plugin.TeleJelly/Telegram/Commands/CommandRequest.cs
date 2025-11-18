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
public class CommandRequest : ICommandBase
{
    private const int MaxRequestsPerUser = 5;

    /// <inheritdoc />
    public string Command => "request";

    /// <inheritdoc />
    public bool NeedsAdmin => false;

    /// <inheritdoc />
    public async Task Execute(
        TelegramBotService telegramBotService,
        Message message,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var botClient = telegramBotService._client;
        var requestService = telegramBotService._serviceProvider.GetRequiredService<RequestService>();

        var imdbId = GetImdbIdArgument(message.Text);

        if (string.IsNullOrWhiteSpace(imdbId))
        {
            // No argument: print current request list (from disk-backed service)
            var listText = await BuildRequestListMessageAsync(requestService, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(listText))
            {
                await botClient.SendMessage(
                    message.Chat.Id,
                    "No requests yet. Use: /request <imdb_id>",
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: cancellationToken);
                return;
            }

            await botClient.SendMessage(
                message.Chat.Id,
                listText,
                ParseMode.MarkdownV2,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: cancellationToken);
            return;
        }

        // We have an imdbId: try to resolve and add a new request
        var userId = message.From?.Id.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        var userDisplayName = GetUserDisplayName(message.From);

        var providerManager = telegramBotService._serviceProvider.GetRequiredService<IProviderManager>();

        // Try to find metadata remotely via Jellyfin's configured providers
        var (title, year, typeName, found) = await MetadataResolver.FindRemoteMetadataAsync(providerManager, imdbId, cancellationToken)
            .ConfigureAwait(false);

        if (!found)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                $"Could not find any movie or series metadata for IMDb id \"{TelegramMarkdown.Escape(imdbId)}\".",
                ParseMode.MarkdownV2,
                linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                cancellationToken: cancellationToken);
            return;
        }

        // Construct an extra link manually since we don't have a local item with populated ProviderIds
        var extraInfo = $" \\- [IMDb]({TelegramMarkdown.Escape($"https://www.imdb.com/title/{imdbId}/")})";

        var request = new MediaRequest
        {
            ItemId = Guid.Empty, // Remote item, not in library
            ImdbId = imdbId,
            Title = title,
            Year = year,
            TypeName = typeName,
            ExtraInfo = extraInfo,
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
                await botClient.SendMessage(
                    message.Chat.Id,
                    TelegramMarkdown.Escape(msg),
                    ParseMode.MarkdownV2,
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: cancellationToken);
                return;
            }

            case RequestAddResult.Duplicate:
            {
                var msg = $"This IMDb id \"{imdbId}\" is already in the request list.";
                await botClient.SendMessage(
                    message.Chat.Id,
                    TelegramMarkdown.Escape(msg),
                    ParseMode.MarkdownV2,
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: cancellationToken);
                return;
            }

            case RequestAddResult.Added:
            {
                var safeTitle = TelegramMarkdown.Escape(title);
                var imdbUrl = $"https://www.imdb.com/title/{imdbId}/";
                var safeImdbUrl = TelegramMarkdown.Escape(imdbUrl);

                var successText = new StringBuilder();
                successText.AppendLine(TelegramMarkdown.Escape("Request added:"))
                    .Append("\\- ")
                    .Append('[')
                    .Append(safeTitle)
                    .Append("](")
                    .Append(safeImdbUrl)
                    .Append(')')
                    .AppendLine();

                if (!string.IsNullOrEmpty(typeName))
                {
                    successText.Append(TelegramMarkdown.Escape($" – {typeName}"));
                }

                if (year.HasValue)
                {
                    successText.Append(TelegramMarkdown.Escape($" ({year.Value})"));
                }

                await botClient.SendMessage(
                    message.Chat.Id,
                    successText.ToString(),
                    ParseMode.MarkdownV2,
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: cancellationToken);
                return;
            }

            default:
            {
                var msg = "An error occurred while adding the request.";
                await botClient.SendMessage(
                    message.Chat.Id,
                    TelegramMarkdown.Escape(msg),
                    ParseMode.MarkdownV2,
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
                    cancellationToken: cancellationToken);
                return;
            }
        }
    }

    private static string GetImdbIdArgument(string? messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return string.Empty;
        }

        // messageText can be:
        // "/request"
        // "/request@BotName"
        // "/request tt1234567"
        // "/request@BotName tt1234567"
        // "/request https://www.imdb.com/title/tt1234567/"
        // "/request@BotName https://www.imdb.com/title/tt1234567/"
        var parts = messageText.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return string.Empty;
        }

        var argument = parts[1].Trim();

        // Check if argument is a URL
        if (Uri.TryCreate(argument, UriKind.Absolute, out var uri))
        {
            // Extract IMDb ID from URL patterns like:
            // https://www.imdb.com/title/tt1234567/
            // https://www.imdb.com/title/tt1234567
            // https://imdb.com/title/tt1234567/
            if (uri.Host.EndsWith("imdb.com", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < segments.Length - 1; i++)
                {
                    if (segments[i].Equals("title", StringComparison.OrdinalIgnoreCase) &&
                        segments[i + 1].StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                    {
                        return segments[i + 1];
                    }
                }
            }
        }

        return argument;
    }

    private static async Task<string?> BuildRequestListMessageAsync(
        RequestService requestService,
        CancellationToken cancellationToken)
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
        foreach (var mediaRequest in snapshot
                     .OrderByDescending(r => r.RequestedAtUtc))
        {
            var indexPrefix = $"{index++}. ";
            sb.Append(TelegramMarkdown.Escape(indexPrefix));

            // Title [linked to IMDb]
            sb.Append('[')
                .Append(TelegramMarkdown.Escape(mediaRequest.Title))
                .Append("](")
                .Append(TelegramMarkdown.Escape($"https://www.imdb.com/title/{mediaRequest.ImdbId}/"))
                .Append(')');

            if (!string.IsNullOrEmpty(mediaRequest.TypeName))
            {
                sb.Append(TelegramMarkdown.Escape($" – {mediaRequest.TypeName}"));
            }

            if (mediaRequest.Year.HasValue)
            {
                sb.Append(TelegramMarkdown.Escape($" ({mediaRequest.Year.Value})"));
            }

            if (!string.IsNullOrWhiteSpace(mediaRequest.ExtraInfo))
            {
                // assume already escaped properly.
                sb.Append(mediaRequest.ExtraInfo);
            }

            // Requested by + date (UTC)
            var requestedBy = TelegramMarkdown.Escape(mediaRequest.UserDisplayName);
            var dateText = TelegramMarkdown.Escape(mediaRequest.RequestedAtUtc.ToString("u", CultureInfo.InvariantCulture));
            sb.AppendLine();
            sb.Append(TelegramMarkdown.Escape("   Requested by: "))
                .Append(requestedBy)
                .Append(TelegramMarkdown.Escape(" at "))
                .Append('`')
                .Append(dateText)
                .Append('`')
                .AppendLine()
                .AppendLine();
        }

        return sb.ToString();
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
}
