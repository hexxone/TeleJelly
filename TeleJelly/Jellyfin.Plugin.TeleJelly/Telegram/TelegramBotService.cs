#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Jellyfin.Plugin.TeleJelly.Telegram.Commands;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

#endregion

namespace Jellyfin.Plugin.TeleJelly.Telegram;

public interface ITelegramBotService : IDisposable
{
    ILogger Logger { get; }

    IServiceProvider ServiceProvider { get; }

    ICommandBase[] Commands { get; }

    TelegramBotClientWrapper BotClientWrapper { get; }


    PluginConfiguration Config { get; set; }

    User? BotInfo { get; set; }

    DateTime? StartTime { get; set; }

    DateTime LastActivityTime { get; set; }
}

/// <summary>
///     The TeleJelly Telegram Bot service which runs in the background and listens for events and commands.
///     Should get re-initialized when the botToken changes.
/// </summary>
internal sealed class TelegramBotService : ITelegramBotService
{
    private readonly string _botToken;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ILibraryManager _libraryManager;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, PendingTorrent> _pendingTorrentUploads = new();


    /// <summary>
    ///     Constructs a new instance of the BotService.
    /// </summary>
    internal TelegramBotService(ILogger logger, string botToken,
        PluginConfiguration config, IServiceProvider serviceProvider,
        TelegramBotClientWrapper botClientWrapper, ICommandBase[] commands,
        ILibraryManager libraryManager)
    {
        Logger = logger;
        _libraryManager = libraryManager;
        _botToken = botToken;
        _cancellationTokenSource = new CancellationTokenSource();

        Config = config;
        ServiceProvider = serviceProvider;
        BotClientWrapper = botClientWrapper;
        Commands = commands;

        logger.LogInformation("{PluginName} Service: {ServiceName} initialized.", nameof(TeleJellyPlugin), nameof(TelegramBotService));
    }

    public ILogger Logger { get; }
    public IServiceProvider ServiceProvider { get; }
    public ICommandBase[] Commands { get; }
    public TelegramBotClientWrapper BotClientWrapper { get; }

    public PluginConfiguration Config { get; set; }
    public User? BotInfo { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime LastActivityTime { get; set; }

    /// <summary>
    ///     Game-End the bot.
    /// </summary>
    public void Dispose()
    {
        StartTime = null;
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }

    /// <summary>
    ///     Needs to be called manually on Config-Change, because the original object reference doesn't get updated.
    ///     Not sure if we could use something like IOptionsMonitor instead ?
    /// </summary>
    /// <param name="configuration"></param>
    public void UpdateConfig(PluginConfiguration configuration)
    {
        Config = configuration;
    }

    /// <summary>
    ///     Starts polling for bot messages.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            BotClientWrapper.Client = new TelegramBotClient(_botToken);

            BotClientWrapper.Client.StartReceiving(
                HandleUpdateAsync,
                HandlePollingErrorAsync,
                cancellationToken: _cancellationTokenSource.Token
            );

            BotInfo = await BotClientWrapper.Client.GetMe();
            Logger.LogInformation("Telegram Bot listening as @{UserName}", BotInfo.Username);
            StartTime = DateTime.UtcNow;
            LastActivityTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to Start Telegram Bot: {Msg}", ex.Message);
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (BotInfo == null)
            {
                throw new Exception($"No bot info available in: {nameof(TelegramBotService)}.{nameof(HandleUpdateAsync)}");
            }

            // Update last activity time on any message received
            LastActivityTime = DateTime.UtcNow;

            switch (update)
            {
                // Handle chat member updates
                case { Type: UpdateType.ChatMember, ChatMember: not null }:
                {
                    var needsConfigSave = await HandleChatMemberUpdate(update, cancellationToken);
                    if (needsConfigSave)
                    {
                        // Manually test saving the config by:
                        // 1. Triggering a ChatMemberUpdate event (e.g., by adding a user to a group).
                        // 2. Verifying that the plugin's configuration file is updated with the new data.
                        TeleJellyPlugin.Instance!.SaveConfiguration(Config);
                    }

                    break;
                }
                // Handle commands
                case { Type: UpdateType.Message, Message.Text: not null }:
                    await HandleBotMessage(update, cancellationToken);
                    break;
                // Handle document uploads (.torrent files)
                case { Type: UpdateType.Message, Message.Document: not null }:
                    await HandleDocumentUpload(update.Message, cancellationToken);
                    break;
                // Handle callback queries from inline keyboards
                case { Type: UpdateType.CallbackQuery, CallbackQuery: not null }:
                    await HandleCallbackQuery(update.CallbackQuery, cancellationToken);
                    break;
                // Handle inline queries for media search
                case { Type: UpdateType.InlineQuery, InlineQuery: not null }:
                    await HandleInlineQuery(update.InlineQuery, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("Error handling update: {ErrMsg}", ex.Message);
        }
    }

    /// <summary>
    ///     Handle a chat member update message
    /// </summary>
    /// <param name="update"></param>
    /// <param name="cancellationToken"></param>
    /// <returns> TRUE if the Config needs to be saved. </returns>
    private async Task<bool> HandleChatMemberUpdate(Update update, CancellationToken cancellationToken)
    {
        Logger.LogDebug("Bot received Update type: {Type}", update.Type);

        var member = update.ChatMember!;
        var user = member.NewChatMember.User;
        var groupId = member.Chat.Id;

        var telegramGroup = Config.TelegramGroups.FirstOrDefault(g => g.TelegramGroupChat?.TelegramChatId == groupId);

        if (string.IsNullOrEmpty(user.Username))
        {
            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.SendMessage(
                    groupId,
                    $"Warning: User '{user.FirstName} {user.LastName}' does not have a Telegram username set. " +
                    "They need to set a username before using TeleJelly login.",
                    cancellationToken: cancellationToken);
            }

            Logger.LogInformation("User Id '{UserId}' has caused a Group ChatMember event but has no Telegram username set.", user.Id);
            return false;
        }

        // User added to group
        if (member.NewChatMember.Status == ChatMemberStatus.Member)
        {
            if (telegramGroup == null)
            {
                if (user.Id == BotInfo?.Id)
                {
                    if (BotClientWrapper.Client != null)
                    {
                        await BotClientWrapper.Client.SendMessage(
                            groupId,
                            Constants.GroupWelcomeMessage,
                            cancellationToken: cancellationToken);
                    }
                }
                else
                {
                    if (BotClientWrapper.Client != null)
                    {
                        await BotClientWrapper.Client.SendMessage(
                            groupId,
                            "This group is not linked to Jellyfin. Please ask an administrator to link this group using the `/link` command.",
                            cancellationToken: cancellationToken);
                    }
                }

                return false;
            }

            if (telegramGroup.TelegramGroupChat!.SyncUserNames && !telegramGroup.UserNames.Contains(user.Username))
            {
                // add Jellyfin Public-Url to Msg if set
                var baseUrl = Config.LoginBaseUrl;
                var serverUrl = baseUrl != null ? $"\nServer URL: {baseUrl}" : "";

                telegramGroup.UserNames.Add(user.Username);
                if (BotClientWrapper.Client != null)
                {
                    await BotClientWrapper.Client.SendMessage(
                        groupId,
                        $"Welcome @{user.Username}! You have been added to the TeleJelly whitelist. {serverUrl}",
                        cancellationToken: cancellationToken);
                }

                Logger.LogInformation("Added @{UserName} to TeleJelly group '{Group}'", user.Username, telegramGroup.GroupName);

                return true;
            }
        }
        // User removed from group
        else if (member.NewChatMember.Status is ChatMemberStatus.Left or ChatMemberStatus.Kicked)
        {
            if (telegramGroup == null || user.Username == null)
            {
                return false;
            }

            if (user.Id == BotInfo?.Id)
            {
                Config.TelegramGroups.Remove(telegramGroup);
                var adminMentions = string.Join(" ", Config.AdminUserNames.Select(admin => $"@{admin}"));
                var message = $"The bot has been removed from the group '{telegramGroup.GroupName}' and the link has been removed.\n\n{adminMentions}";
                if (BotClientWrapper.Client != null)
                {
                    await BotClientWrapper.Client.SendMessage(
                        groupId,
                        message,
                        cancellationToken: cancellationToken);
                }

                return true;
            }

            if (telegramGroup.TelegramGroupChat!.SyncUserNames && telegramGroup.UserNames.Remove(user.Username))
            {
                if (BotClientWrapper.Client != null)
                {
                    await BotClientWrapper.Client.SendMessage(
                        groupId,
                        $"Removed @{user.Username} from TeleJelly whitelist",
                        cancellationToken: cancellationToken);
                }

                Logger.LogInformation("Removed @{UserName} from TeleJelly group '{Group}'", user.Username, telegramGroup.GroupName);

                return true;
            }
        }

        return false;
    }

    private async Task HandleBotMessage(Update update, CancellationToken cancellationToken)
    {
        if (BotInfo?.Username == null)
        {
            throw new Exception($"No bot info available in: {nameof(TelegramBotService)}.{nameof(HandleBotMessage)}");
        }

        var message = update.Message!;

        // Check if this is a reply to a pending torrent upload
        if (message.ReplyToMessage != null &&
            _pendingTorrentUploads.TryRemove(message.ReplyToMessage.MessageId, out var pendingTorrent))
        {
            await HandleTorrentImdbReply(message, pendingTorrent, cancellationToken);
            return;
        }

        if (message.Text == null || !message.Text.StartsWith('/'))
        {
            return; // Not a command, ignore
        }

        Logger.LogDebug("Bot received Update type: {UpdateType} from UserId: '{FromId}' text: '{MsgText}'", update.Type, message.From?.Id, message.Text);

        var commandText = GetCommandText(message.Text, BotInfo.Username);
        if (commandText == null)
        {
            return; // directed at different bot
        }

        // Find & Execute Bot command
        await FindAndExecuteCommand(message, commandText, cancellationToken);
    }

    private async Task HandleTorrentImdbReply(Message message, PendingTorrent pendingTorrent, CancellationToken cancellationToken)
    {
        var imdbId = message.Text?.Trim();
        if (string.IsNullOrEmpty(imdbId) || !imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.SendMessage(
                    message.Chat.Id,
                    "❌ Invalid IMDB ID format. Please provide a valid ID starting with 'tt' (e.g., tt1234567)",
                    cancellationToken: cancellationToken);
            }

            return;
        }

        try
        {
            // Save torrent to temp location
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), pendingTorrent.FileName);
            await System.IO.File.WriteAllBytesAsync(tempPath, pendingTorrent.FileBytes, cancellationToken);

            Logger.LogInformation("Starting download workflow for torrent {FileName} with IMDB ID {ImdbId}", pendingTorrent.FileName, imdbId);

            // Start download workflow
            var orchestrator = ServiceProvider.GetService<IDownloadOrchestrator>();
            if (orchestrator == null)
            {
                Logger.LogError("DownloadOrchestrator not found in service provider");
                if (BotClientWrapper.Client != null)
                {
                    await BotClientWrapper.Client.SendMessage(
                        message.Chat.Id,
                        "❌ Internal error: Download orchestrator not available",
                        cancellationToken: cancellationToken);
                }

                return;
            }

            var download = await orchestrator.BeginDownloadWorkflow(imdbId, message.Chat.Id, message.From!.Id, $"file://{tempPath}");

            // Get available libraries
            var libraries = _libraryManager.GetVirtualFolders();
            var libraryButtons = libraries
                .Select(lib => new[] { InlineKeyboardButton.WithCallbackData(lib.Name, $"dl_{download.Id}_library_{lib.ItemId}") })
                .ToArray();

            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.SendMessage(
                    message.Chat.Id,
                    $"✅ Download workflow started for <b>{download.Title}</b> ({download.Year})\n\nPlease select the target library:",
                    ParseMode.Html,
                    replyMarkup: new InlineKeyboardMarkup(libraryButtons),
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to start download workflow for torrent");
            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.SendMessage(
                    message.Chat.Id,
                    $"❌ Failed to start download: {ex.Message}",
                    cancellationToken: cancellationToken);
            }
        }
    }

    private async Task HandleCallbackQuery(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        Debug.Assert(BotClientWrapper.Client != null, "BotClientWrapper.Client != null");

        if (callbackQuery.Data == null)
        {
            return;
        }

        Logger.LogInformation("Received callback query: {Data}", callbackQuery.Data);

        var parts = callbackQuery.Data.Split('_');
        if (parts.Length < 3 || parts[0] != "dl" || !Guid.TryParse(parts[1], out var downloadId))
        {
            await BotClientWrapper.Client.AnswerCallbackQuery(callbackQuery.Id, "Invalid callback data.", cancellationToken: cancellationToken);
            return;
        }

        var orchestrator = ServiceProvider.GetService<IDownloadOrchestrator>();
        if (orchestrator == null)
        {
            Logger.LogError("DownloadOrchestrator not found in service provider.");

            await BotClientWrapper.Client.AnswerCallbackQuery(callbackQuery.Id, "Internal server error.", cancellationToken: cancellationToken);
            return;
        }

        var download = orchestrator.GetDownload(downloadId);
        if (download == null)
        {
            await BotClientWrapper.Client.AnswerCallbackQuery(callbackQuery.Id, "Download not found.", cancellationToken: cancellationToken);
            return;
        }

        // User validation: only the download initiator can interact with callbacks
        if (callbackQuery.From?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) != download.UserId)
        {
            await BotClientWrapper.Client.AnswerCallbackQuery(callbackQuery.Id, "Only the download initiator can interact with this.", true, cancellationToken: cancellationToken);
            return;
        }

        var action = parts[2];
        var value = parts.Length > 3 ? parts[3] : null;

        try
        {
            switch (action)
            {
                case "library":
                    download.TargetLibraryId = value;
                    await orchestrator.UpdateDownloadStatus(downloadId, DownloadStatus.AwaitingMediaType);

                    var mediaTypeKeyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Movie", $"dl_{download.Id}_mediatype_Movie"), InlineKeyboardButton.WithCallbackData("Series", $"dl_{download.Id}_mediatype_Series"), InlineKeyboardButton.WithCallbackData("Cancel", $"dl_{download.Id}_cancel"));

                    await BotClientWrapper.Client.EditMessageText(
                        callbackQuery.Message!.Chat.Id,
                        callbackQuery.Message.MessageId,
                        "Please select the media type:",
                        replyMarkup: mediaTypeKeyboard,
                        cancellationToken: cancellationToken);
                    break;

                case "mediatype":
                    download.MediaType = Enum.Parse<MediaType>(value!);

                    // If Series, prompt for season selection
                    if (download.MediaType == MediaType.Series)
                    {
                        await orchestrator.UpdateDownloadStatus(downloadId, DownloadStatus.AwaitingSeason);

                        var seasonButtons = new List<InlineKeyboardButton[]>();
                        for (int i = 1; i <= 10; i++)
                        {
                            if (i % 2 == 1)
                            {
                                var buttons = new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData($"Season {i}", $"dl_{download.Id}_season_{i}") };
                                if (i + 1 <= 10)
                                {
                                    buttons.Add(InlineKeyboardButton.WithCallbackData($"Season {i + 1}", $"dl_{download.Id}_season_{i + 1}"));
                                }

                                seasonButtons.Add(buttons.ToArray());
                            }
                        }

                        seasonButtons.Add([InlineKeyboardButton.WithCallbackData("Cancel", $"dl_{download.Id}_cancel")]);

                        await BotClientWrapper.Client.EditMessageText(
                            callbackQuery.Message!.Chat.Id,
                            callbackQuery.Message.MessageId,
                            $"Media type set to <b>Series</b>. Please select the season:",
                            ParseMode.Html,
                            replyMarkup: new InlineKeyboardMarkup(seasonButtons),
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        // Movie: proceed to path confirmation
                        await ShowPathConfirmation(orchestrator, download, callbackQuery, cancellationToken);
                    }

                    break;

                case "season":
                    if (int.TryParse(value, out var seasonNum))
                    {
                        download.Season = seasonNum;
                        await ShowPathConfirmation(orchestrator, download, callbackQuery, cancellationToken);
                    }

                    break;

                case "accept":
                    // Accept the path and initiate download
                    download.UserConfirmedPath = download.SuggestedDestinationPath;
                    var success = await orchestrator.InitiateDownloadAsync(downloadId, cancellationToken);

                    if (BotClientWrapper.Client != null)
                    {
                        if (success)
                        {
                            await BotClientWrapper.Client.EditMessageText(
                                callbackQuery.Message!.Chat.Id,
                                callbackQuery.Message.MessageId,
                                $"✅ Download started for <b>{download.Title}</b>!\nPath: <code>{download.SuggestedDestinationPath}</code>",
                                ParseMode.Html,
                                cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await BotClientWrapper.Client.EditMessageText(
                                callbackQuery.Message!.Chat.Id,
                                callbackQuery.Message.MessageId,
                                $"❌ Failed to start download. {download.ErrorMessage ?? "No available download service."}",
                                ParseMode.Html,
                                cancellationToken: cancellationToken);
                        }
                    }

                    break;

                case "edit":
                    // Prompt user to reply with custom path
                    await orchestrator.UpdateDownloadStatus(downloadId, DownloadStatus.AwaitingPathConfirm);
                    if (BotClientWrapper.Client != null)
                    {
                        await BotClientWrapper.Client.SendMessage(
                            callbackQuery.Message!.Chat.Id,
                            "Please reply to this message with your custom path:",
                            replyMarkup: new ForceReplyMarkup(),
                            cancellationToken: cancellationToken);
                    }

                    break;

                case "cancel":
                    await orchestrator.UpdateDownloadStatus(downloadId, DownloadStatus.Canceled);
                    if (BotClientWrapper.Client != null)
                    {
                        await BotClientWrapper.Client.EditMessageText(
                            callbackQuery.Message!.Chat.Id,
                            callbackQuery.Message.MessageId,
                            $"Download for <b>{download.Title}</b> has been canceled.",
                            ParseMode.Html,
                            cancellationToken: cancellationToken);
                    }

                    break;

                default:
                    if (BotClientWrapper.Client != null)
                    {
                        await BotClientWrapper.Client.AnswerCallbackQuery(callbackQuery.Id, "Unknown action.", cancellationToken: cancellationToken);
                    }

                    break;
            }

            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing callback query for download {DownloadId}", downloadId);
            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.AnswerCallbackQuery(callbackQuery.Id, "An error occurred.", cancellationToken: cancellationToken);
            }
        }
    }

    private async Task ShowPathConfirmation(IDownloadOrchestrator orchestrator, ManagedDownload download, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        Debug.Assert(BotClientWrapper.Client != null, "BotClientWrapper.Client != null");

        await orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.AwaitingPathConfirm);

        var library = _libraryManager.GetItemById(download.TargetLibraryId!);
        if (library?.Path == null)
        {
            await orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.Failed,
                "Target library not found or path is missing.");
            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.EditMessageText(
                    callbackQuery.Message!.Chat.Id,
                    callbackQuery.Message.MessageId,
                    "❌ Error: Target library not found or path is missing.",
                    ParseMode.Html,
                    cancellationToken: cancellationToken);
            }

            return;
        }

        // Build proposed path
        var pathTemplater = ServiceProvider.GetService(typeof(PathTemplateService)) as PathTemplateService;
        var config = TeleJellyPlugin.Instance!.Configuration.DownloadManager;
        var librarySettings = config.LibrarySettings.FirstOrDefault(l => l.LibraryId == download.TargetLibraryId) ?? new LibrarySettings();

        var proposedPath = library.Path;
        if (pathTemplater != null)
        {
            try
            {
                proposedPath = await pathTemplater.ApplyTemplateAsync(
                    librarySettings.PathTemplate,
                    download,
                    new Dictionary<string, string>(),
                    download.Title);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to apply path template, using library path");
            }
        }

        download.SuggestedDestinationPath = proposedPath;

        var confirmKeyboard = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("✅ Accept", $"dl_{download.Id}_accept")],
            [InlineKeyboardButton.WithCallbackData("✏️ Edit Path", $"dl_{download.Id}_edit")],
            [InlineKeyboardButton.WithCallbackData("❌ Cancel", $"dl_{download.Id}_cancel")]
        ]);

        var seasonInfo = download.Season.HasValue ? $"\n<b>Season:</b> {download.Season}" : "";
        await BotClientWrapper.Client.EditMessageText(
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            $"<b>Download Ready</b>\n\n" +
            $"<b>Title:</b> {download.Title} ({download.Year})\n" +
            $"<b>Type:</b> {download.MediaType}{seasonInfo}\n" +
            $"<b>Path:</b> <code>{proposedPath}</code>\n\n" +
            $"Please confirm or edit the download path:",
            ParseMode.Html,
            replyMarkup: confirmKeyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleInlineQuery(InlineQuery inlineQuery, CancellationToken cancellationToken)
    {
        Debug.Assert(BotClientWrapper.Client != null, "BotClientWrapper.Client != null");

        // Check if inline queries are enabled
        if (!Config.EnableInlineQueries)
        {
            Logger.LogDebug("Inline queries are disabled, ignoring query from @{Username}", inlineQuery.From.Username);
            await BotClientWrapper.Client.AnswerInlineQuery(
                inlineQuery.Id,
                [],
                cacheTime: 300,
                cancellationToken: cancellationToken);
            return;
        }

        var username = inlineQuery.From.Username;
        Logger.LogDebug("Received inline query from @{Username}: {Query}", username, inlineQuery.Query);

        // Create search service
        var searchService = new MediaSearchService(_libraryManager);

        // Check if user is authorized (admin or member of any group)
        if (!searchService.IsUserAuthorizedForInlineQuery(Config, username))
        {
            Logger.LogInformation("Unauthorized inline query from @{Username}", username);
            await BotClientWrapper.Client.AnswerInlineQuery(
                inlineQuery.Id,
                [],
                cacheTime: 300,
                cancellationToken: cancellationToken);
            return;
        }

        // Get user's library access
        var (allowAllLibraries, allowedLibraryIds) = searchService.GetUserLibraryAccess(Config, username);

        // Perform search
        var queryText = inlineQuery.Query.Trim();
        if (string.IsNullOrWhiteSpace(queryText))
        {
            await BotClientWrapper.Client.AnswerInlineQuery(
                inlineQuery.Id,
                [],
                cacheTime: 10,
                cancellationToken: cancellationToken);
            return;
        }

        var searchResult = searchService.Search(queryText, allowedLibraryIds, allowAllLibraries, maxResults: 20);

        if (searchResult.Items.Count == 0)
        {
            await BotClientWrapper.Client.AnswerInlineQuery(
                inlineQuery.Id,
                [],
                cacheTime: 60,
                cancellationToken: cancellationToken);
            return;
        }

        // Build inline query results
        var baseUrl = Config.LoginBaseUrl;
        var results = new List<InlineQueryResult>();

        foreach (var item in searchResult.Items)
        {
            var itemUrl = MediaSearchService.GetJellyfinItemUrl(baseUrl, item.Id);
            var year = item.ProductionYear?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A";
            var title = item.Name ?? "Unknown";

            // Create button text: "Watch <Title> (<Year>) in Jellyfin"
            var buttonText = $"Watch {title} ({year}) in Jellyfin";

            // Create description with media info
            var description = item.GetDisplayText();

            // Create inline keyboard with the link button
            InlineKeyboardMarkup? replyMarkup = null;
            if (!string.IsNullOrWhiteSpace(itemUrl))
            {
                replyMarkup = new InlineKeyboardMarkup(
                    InlineKeyboardButton.WithUrl(buttonText, itemUrl));
            }

            // Create article result
            var resultId = $"search_{item.Id:N}";
            var articleResult = new InlineQueryResultArticle
            {
                Id = resultId,
                Title = $"{title} ({year})",
                Description = description,
                InputMessageContent = new InputTextMessageContent
                {
                    MessageText = !string.IsNullOrWhiteSpace(itemUrl)
                        ? $"Watch [{title} ({year})]({itemUrl}) in Jellyfin"
                        : $"{title} ({year})",
                    ParseMode = ParseMode.Markdown,
                    LinkPreviewOptions = new LinkPreviewOptions { IsDisabled = true }
                },
                ReplyMarkup = replyMarkup
            };

            results.Add(articleResult);
        }

        await BotClientWrapper.Client.AnswerInlineQuery(
            inlineQuery.Id,
            results,
            cacheTime: 60,
            cancellationToken: cancellationToken);

        Logger.LogDebug("Answered inline query with {Count} results for @{Username}", results.Count, username);
    }

    private async Task FindAndExecuteCommand(Message message, string commandText, CancellationToken cancellationToken)
    {
        try
        {
            var isAdmin = message.From?.Username != null && Config.AdminUserNames.Contains(message.From.Username);

            var commandFound = false;
            foreach (var command in Commands)
            {
                if (!command.Command.Equals(commandText, StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                commandFound = true;

                if (command.NeedsAdmin && !isAdmin)
                {
                    if (BotClientWrapper.Client != null)
                    {
                        await BotClientWrapper.Client.SendMessage(
                            message.Chat.Id,
                            "You are not an administrator.",
                            cancellationToken: cancellationToken);
                    }

                    break;
                }

                Logger.LogDebug("Executing command: {Command}", command.Command);
                await command.Execute(this, message, isAdmin, cancellationToken);
                break;
            }

            if (!commandFound && BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.SendMessage(message.Chat.Id, "Unknown command.", cancellationToken: cancellationToken);
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "An error occured while executing command : {Command}", commandText);
            throw;
        }
    }

    private static string? GetCommandText(string messageText, string botUsername)
    {
        // Strip "/" slash and get the first word as a command
        var commandText = messageText[1..];

        // If contains spaces, get first word as command
        var spaceIndex = commandText.IndexOf(' ');
        if (spaceIndex > 0)
        {
            commandText = commandText[..spaceIndex];
        }

        // Handle directed bot commands (e.g., /command@botname)
        // If a command is directed at a different bot, ignore it
        if (commandText.Contains('@'))
        {
            var parts = commandText.Split('@', 2);
            var targetBotUsername = parts[1];

            if (!string.Equals(targetBotUsername, botUsername, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            commandText = parts[0]; // Keep only the command part
        }

        return commandText;
    }

    private async Task HandleDocumentUpload(Message message, CancellationToken cancellationToken)
    {
        if (message.Document?.FileName?.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) != true)
        {
            return; // Not a torrent file, ignore
        }

        Logger.LogInformation("Received .torrent file upload: {FileName}", message.Document.FileName);
        Debug.Assert(BotClientWrapper.Client != null, "BotClientWrapper.Client != null");

        try
        {
            // Download the file
            var file = await BotClientWrapper.Client.GetFile(message.Document.FileId, cancellationToken);
            using var stream = new System.IO.MemoryStream();
            await BotClientWrapper.Client.DownloadFile(file.FilePath!, stream, cancellationToken);
            var bytes = stream.ToArray();

            // Store temporarily
            _pendingTorrentUploads[message.MessageId] = new PendingTorrent
            {
                FileName = message.Document.FileName,
                FileBytes = bytes,
                UserId = message.From!.Id,
                ChatId = message.Chat.Id,
                UploadedAt = DateTime.UtcNow
            };

            // Prompt for IMDB ID
            await BotClientWrapper.Client.SendMessage(
                message.Chat.Id,
                "✅ Torrent file received! Please reply to this message with the IMDB ID (e.g., tt1234567)",
                replyMarkup: new ForceReplyMarkup { Selective = true },
                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                cancellationToken: cancellationToken);

            Logger.LogInformation("Waiting for IMDB ID reply for torrent: {FileName}", message.Document.FileName);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process torrent file upload");
            await BotClientWrapper.Client.SendMessage(
                message.Chat.Id,
                "❌ Failed to process torrent file. Please try again.",
                cancellationToken: cancellationToken);
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException => $"Telegram API Error: {apiRequestException.Message}",
            _ => exception.ToString()
        };

        Logger.LogError("Bot update handling Error: {Err}", errorMessage);

        return Task.CompletedTask;
    }

    private sealed class PendingTorrent
    {
        public string FileName { get; init; } = string.Empty;
        public byte[] FileBytes { get; init; } = [];
        public long UserId { get; init; }
        public long ChatId { get; init; }
        public DateTime UploadedAt { get; init; }
    }
}
