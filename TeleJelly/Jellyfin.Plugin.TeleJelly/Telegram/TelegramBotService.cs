#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration.Library;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Jellyfin.Plugin.TeleJelly.Services.Download.Hosted;
using Jellyfin.Plugin.TeleJelly.Services.Download.Search;
using Jellyfin.Plugin.TeleJelly.Telegram.Commands;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
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

    Task RegisterFailedDownloadReplyAsync(int messageId, Guid downloadId, CancellationToken cancellationToken);

    Task StartDownloadSelectionAsync(ManagedDownload download, CancellationToken cancellationToken);
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, PendingDownloadFile> _pendingDownloadFileUploads = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, PendingExtractionRetry> _pendingExtractionRetries = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, FailedDownloadReply> _failedDownloadReplies = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _activeDownloadCallbacks = new();


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

    public async Task RegisterFailedDownloadReplyAsync(int messageId, Guid downloadId, CancellationToken cancellationToken)
    {
        var registeredAt = DateTime.UtcNow;
        _failedDownloadReplies[messageId] = new FailedDownloadReply
        {
            DownloadId = downloadId,
            RegisteredAt = registeredAt
        };

        var orchestrator = ServiceProvider.GetService<IDownloadOrchestrator>();
        if (orchestrator != null)
        {
            await orchestrator.SetTelegramMessageAsync(downloadId, messageId, registeredAt, cancellationToken);
        }
    }

    public async Task StartDownloadSelectionAsync(ManagedDownload download, CancellationToken cancellationToken)
    {
        var client = BotClientWrapper.Client ?? throw new InvalidOperationException("Telegram bot client is unavailable.");
        var orchestrator = ServiceProvider.GetService<IDownloadOrchestrator>() ??
                           throw new InvalidOperationException("Download orchestrator is unavailable.");
        var libraries = DownloadLibrarySelection.GetLibraries(_libraryManager);
        if (libraries.Count == 0)
        {
            throw new InvalidOperationException("No libraries are configured in Jellyfin.");
        }

        var automaticLibrary = DownloadLibrarySelection.SelectAutomaticLibrary(libraries, download.MediaType);
        if (automaticLibrary != null)
        {
            download.TargetLibraryId = automaticLibrary.Id.ToString();
            var automaticMessage = await client.SendMessage(
                download.ChatId,
                $"✅ <b>{System.Net.WebUtility.HtmlEncode(download.Title)}</b> ({download.Year})\n" +
                $"Target library: <b>{System.Net.WebUtility.HtmlEncode(automaticLibrary.Name)}</b> (selected automatically)",
                ParseMode.Html,
                cancellationToken: cancellationToken);
            await orchestrator.SetTelegramMessageAsync(
                download.Id,
                automaticMessage.MessageId,
                automaticMessage.Date.ToUniversalTime(),
                cancellationToken);
            await AdvanceAfterLibrarySelectionAsync(
                orchestrator,
                download,
                automaticMessage.Chat.Id,
                automaticMessage.MessageId,
                cancellationToken);
            return;
        }

        var selectableLibraries = DownloadLibrarySelection.GetSelectableLibraries(libraries, download.MediaType);
        var rows = selectableLibraries
            .Select(library => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    library.Name,
                    DownloadFlowPresentation.CreateLibraryCallbackData(download.Id, library.Id))
            })
            .ToList();
        rows.Add([InlineKeyboardButton.WithCallbackData("Cancel", $"dl_{download.Id}_cancel")]);

        var workflowMessage = await client.SendMessage(
            download.ChatId,
            $"Starting download for <b>{System.Net.WebUtility.HtmlEncode(download.Title)} ({download.Year})</b>.\n\nPlease select the destination library:",
            ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(rows),
            cancellationToken: cancellationToken);
        await orchestrator.SetTelegramMessageAsync(
            download.Id,
            workflowMessage.MessageId,
            workflowMessage.Date.ToUniversalTime(),
            cancellationToken);
    }

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

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new[]
                {
                    UpdateType.Message,
                    UpdateType.CallbackQuery,
                    UpdateType.InlineQuery,
                    UpdateType.ChatMember
                }
            };

            BotClientWrapper.Client.StartReceiving(
                HandleUpdateAsync,
                HandlePollingErrorAsync,
                receiverOptions,
                _cancellationTokenSource.Token
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
                // Handle supported download container uploads (.torrent and .dlc files)
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

        if (message.ReplyToMessage != null &&
            !string.IsNullOrWhiteSpace(message.Text) &&
            !message.Text.StartsWith('/') &&
            TryGetFailedDownloadReply(message, out var failedDownloadReply))
        {
            if (!DownloadFlowPresentation.TryParseManualDownloadSource(message.Text, out var source))
            {
                if (BotClientWrapper.Client != null)
                {
                    await BotClientWrapper.Client.SendMessage(
                        message.Chat.Id,
                        "❌ Please reply with a complete HTTP(S) URL, magnet link, `.torrent`, or `.dlc` file.",
                        cancellationToken: cancellationToken);
                }

                return;
            }

            _failedDownloadReplies.TryRemove(message.ReplyToMessage.MessageId, out _);
            await HandleFailedDownloadSourceReply(
                message,
                message.ReplyToMessage.MessageId,
                failedDownloadReply,
                source!,
                cancellationToken,
                []);
            return;
        }

        // Check if this is a reply to a pending download-container upload.
        if (message.ReplyToMessage != null &&
            _pendingDownloadFileUploads.TryRemove(message.ReplyToMessage.MessageId, out var pendingDownloadFile))
        {
            await HandleDownloadFileImdbReply(message, pendingDownloadFile, cancellationToken);
            return;
        }

        if (message.ReplyToMessage != null &&
            _pendingExtractionRetries.TryRemove(message.ReplyToMessage.MessageId, out var pendingExtractionRetry))
        {
            await HandleExtractionRetryReply(message, pendingExtractionRetry, cancellationToken);
            return;
        }

        if (message.ReplyToMessage != null && !string.IsNullOrWhiteSpace(message.Text))
        {
            var orchestratorForPathEdit = ServiceProvider.GetService<IDownloadOrchestrator>();
            if (orchestratorForPathEdit != null && message.From != null)
            {
                var pendingPathEdit = orchestratorForPathEdit.GetAllDownloads()
                    .Where(d =>
                        d.ChatId == message.Chat.Id &&
                        d.UserId == message.From.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                        d.Status == DownloadStatus.AwaitingPathConfirm)
                    .OrderByDescending(d => d.StartedAt)
                    .FirstOrDefault();

                if (pendingPathEdit != null)
                {
                    try
                    {
                        var rawPath = message.Text.Trim();
                        var library = string.IsNullOrWhiteSpace(pendingPathEdit.TargetLibraryId)
                            ? null
                            : _libraryManager.GetItemById(pendingPathEdit.TargetLibraryId);
                        var pathTemplater = ServiceProvider.GetService<PathTemplateService>();

                        if (library?.Path == null || pathTemplater == null)
                        {
                            if (BotClientWrapper.Client != null)
                            {
                                await BotClientWrapper.Client.SendMessage(
                                    message.Chat.Id,
                                    "❌ Could not resolve the target library path for this download.",
                                    cancellationToken: cancellationToken);
                            }

                            return;
                        }

                        var resolvedPath = await pathTemplater.ResolvePathAsync(library.Path, rawPath);
                        if (!await pathTemplater.ValidatePathAsync(resolvedPath))
                        {
                            if (BotClientWrapper.Client != null)
                            {
                                await BotClientWrapper.Client.SendMessage(
                                    message.Chat.Id,
                                    "❌ The provided path is invalid.",
                                    cancellationToken: cancellationToken);
                            }

                            return;
                        }

                        pendingPathEdit.UserConfirmedPath = resolvedPath;
                        pendingPathEdit.SuggestedDestinationPath = resolvedPath;
                        var started = await orchestratorForPathEdit.InitiateDownloadAsync(pendingPathEdit.Id, cancellationToken);
                        if (BotClientWrapper.Client != null)
                        {
                            var resultMessage = await BotClientWrapper.Client.SendMessage(
                                message.Chat.Id,
                                started
                                    ? $"✅ Download started for <b>{pendingPathEdit.Title}</b>.\nPath: <code>{pendingPathEdit.UserConfirmedPath}</code>"
                                    : $"❌ Failed to start download: {System.Net.WebUtility.HtmlEncode(DownloadFailureGuidance.AppendReplyOption(pendingPathEdit.ErrorMessage ?? "No available service."))}",
                                ParseMode.Html,
                                cancellationToken: cancellationToken);
                            if (!started)
                            {
                                await RegisterFailedDownloadReplyAsync(resultMessage.MessageId, pendingPathEdit.Id, cancellationToken);
                            }
                            else
                            {
                                await orchestratorForPathEdit.SetTelegramMessageAsync(
                                    pendingPathEdit.Id,
                                    resultMessage.MessageId,
                                    resultMessage.Date.ToUniversalTime(),
                                    cancellationToken);
                            }
                        }

                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to process custom path reply for download {DownloadId}", pendingPathEdit.Id);
                        await orchestratorForPathEdit.UpdateDownloadStatus(pendingPathEdit.Id, DownloadStatus.Failed, ex.Message);
                        if (BotClientWrapper.Client != null)
                        {
                            var failureMessage = await BotClientWrapper.Client.SendMessage(
                                message.Chat.Id,
                                $"❌ {DownloadFailureGuidance.AppendReplyOption(pendingPathEdit.ErrorMessage ?? ex.Message)}",
                                cancellationToken: cancellationToken);
                            await RegisterFailedDownloadReplyAsync(failureMessage.MessageId, pendingPathEdit.Id, cancellationToken);
                        }

                        return;
                    }
                }
            }
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

    private async Task HandleFailedDownloadSourceReply(
        Message message,
        int failureMessageId,
        FailedDownloadReply pendingReply,
        string source,
        CancellationToken cancellationToken,
        IEnumerable<string> passwordCandidates)
    {
        var orchestrator = ServiceProvider.GetService<IDownloadOrchestrator>();
        var download = orchestrator?.GetDownload(pendingReply.DownloadId);
        var expectedUserId = message.From?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (orchestrator == null ||
            download == null ||
            DateTime.UtcNow - pendingReply.RegisteredAt > TimeSpan.FromDays(7) ||
            download.ChatId != message.Chat.Id ||
            download.UserId != expectedUserId ||
            download.Status is not (DownloadStatus.Failed or DownloadStatus.ExtractionFailed or DownloadStatus.Stalled))
        {
            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.SendMessage(
                    message.Chat.Id,
                    "❌ This failed download can no longer be retried from that message.",
                    cancellationToken: cancellationToken);
            }

            return;
        }

        try
        {
            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.EditMessageText(
                    message.Chat.Id,
                    failureMessageId,
                    $"⏳ Retrying <b>{System.Net.WebUtility.HtmlEncode(download.Title)}</b> with the supplied source…",
                    ParseMode.Html,
                    cancellationToken: cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(download.UserConfirmedPath))
            {
                download.LinkOrMagnet = source;
                await orchestrator.MergePasswordCandidatesAsync(download.Id, passwordCandidates, cancellationToken);
                await ShowPathConfirmation(
                    orchestrator,
                    download,
                    message.Chat.Id,
                    failureMessageId,
                    cancellationToken);
                return;
            }

            var started = await orchestrator.RetryDownloadWithSourceAsync(download.Id, source, cancellationToken, passwordCandidates);
            if (BotClientWrapper.Client != null)
            {
                if (started)
                {
                    await BotClientWrapper.Client.EditMessageText(
                        message.Chat.Id,
                        failureMessageId,
                        BuildDownloadQueuedText(download, download.UserConfirmedPath),
                        ParseMode.Html,
                        cancellationToken: cancellationToken);
                    await orchestrator.SetTelegramMessageAsync(download.Id, failureMessageId, DateTime.UtcNow, cancellationToken);
                }
                else
                {
                    await RegisterFailedDownloadReplyAsync(failureMessageId, download.Id, cancellationToken);
                    await BotClientWrapper.Client.EditMessageText(
                        message.Chat.Id,
                        failureMessageId,
                        $"❌ {System.Net.WebUtility.HtmlEncode(DownloadFailureGuidance.AppendReplyOption(download.ErrorMessage ?? "Failed to retry download."))}",
                        ParseMode.Html,
                        cancellationToken: cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to retry download {DownloadId} from a Telegram reply", download.Id);
            await orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.Failed, ex.Message);
            await RegisterFailedDownloadReplyAsync(failureMessageId, download.Id, cancellationToken);
            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.EditMessageText(
                    message.Chat.Id,
                    failureMessageId,
                    $"❌ {System.Net.WebUtility.HtmlEncode(DownloadFailureGuidance.AppendReplyOption(download.ErrorMessage ?? ex.Message))}",
                    ParseMode.Html,
                    cancellationToken: cancellationToken);
            }
        }
    }

    private async Task HandleDownloadFileImdbReply(Message message, PendingDownloadFile pendingDownloadFile, CancellationToken cancellationToken)
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

        await StartDownloadFileWorkflow(message, pendingDownloadFile, imdbId, cancellationToken);
    }

    private async Task StartDownloadFileWorkflow(
        Message message,
        PendingDownloadFile pendingDownloadFile,
        string imdbId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Save torrent to temp location
            var tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"telejelly-{Guid.NewGuid():N}-{System.IO.Path.GetFileName(pendingDownloadFile.FileName)}");
            await System.IO.File.WriteAllBytesAsync(tempPath, pendingDownloadFile.FileBytes, cancellationToken);

            Logger.LogInformation("Starting download workflow for container file {FileName} with IMDB ID {ImdbId}", pendingDownloadFile.FileName, imdbId);

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
            await orchestrator.MergePasswordCandidatesAsync(
                download.Id,
                await ExtractDlcPasswordCandidatesAsync(pendingDownloadFile.FileName, pendingDownloadFile.FileBytes, cancellationToken),
                cancellationToken);

            await StartDownloadSelectionAsync(download, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "Failed to start download workflow for container file {FileName} with IMDB ID {ImdbId}, UserId: {UserId}, ChatId: {ChatId}",
                pendingDownloadFile.FileName,
                imdbId,
                message.From?.Id,
                message.Chat.Id);
            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.SendMessage(
                    message.Chat.Id,
                    $"❌ {DownloadFailureGuidance.Append($"Failed to start download: {ex.Message}", imdbId)}",
                    cancellationToken: cancellationToken);
            }
        }
    }

    private async Task HandleExtractionRetryReply(Message message, PendingExtractionRetry pendingRetry, CancellationToken cancellationToken)
    {
        var password = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(password))
        {
            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.SendMessage(
                    message.Chat.Id,
                    "❌ Extraction password cannot be empty.",
                    cancellationToken: cancellationToken);
            }

            return;
        }

        var orchestrator = ServiceProvider.GetService<IDownloadOrchestrator>();
        var download = orchestrator?.GetDownload(pendingRetry.DownloadId);
        if (download == null)
        {
            if (BotClientWrapper.Client != null)
            {
                await BotClientWrapper.Client.SendMessage(message.Chat.Id, "Download no longer available.", cancellationToken: cancellationToken);
            }

            return;
        }

        await orchestrator!.MergePasswordCandidatesAsync(download.Id, [password], cancellationToken);
        await orchestrator!.UpdateDownloadStatus(download.Id, DownloadStatus.Extracting);
        if (BotClientWrapper.Client != null)
        {
            await BotClientWrapper.Client.SendMessage(
                message.Chat.Id,
                $"🔁 Retry started for <b>{download.Title}</b> with provided password.",
                ParseMode.Html,
                cancellationToken: cancellationToken);
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

        if (!DownloadFlowPresentation.TryParseDownloadCallback(callbackQuery.Data, out var downloadId, out var action, out var value))
        {
            await TryAnswerCallbackQuery(callbackQuery.Id, "Invalid callback data.", cancellationToken: cancellationToken);
            return;
        }

        var orchestrator = ServiceProvider.GetService<IDownloadOrchestrator>();
        if (orchestrator == null)
        {
            Logger.LogError("DownloadOrchestrator not found in service provider.");

            await TryAnswerCallbackQuery(callbackQuery.Id, "Internal server error.", cancellationToken: cancellationToken);
            return;
        }

        var download = orchestrator.GetDownload(downloadId);
        if (download == null)
        {
            await TryAnswerCallbackQuery(callbackQuery.Id, "Download not found.", cancellationToken: cancellationToken);
            return;
        }

        // User validation: only the download initiator can interact with callbacks
        if (callbackQuery.From?.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) != download.UserId)
        {
            await TryAnswerCallbackQuery(callbackQuery.Id, "Only the download initiator can interact with this.", true, cancellationToken);
            return;
        }

        if (!_activeDownloadCallbacks.TryAdd(downloadId, 0))
        {
            await TryAnswerCallbackQuery(callbackQuery.Id, "This download action is already being processed.", cancellationToken: cancellationToken);
            return;
        }

        var callbackAnswerAttempted = false;
        try
        {
            if (!IsCallbackActionAllowed(action, download.Status))
            {
                await TryAnswerCallbackQuery(
                    callbackQuery.Id,
                    "This menu is no longer active.",
                    cancellationToken: cancellationToken);
                callbackAnswerAttempted = true;
                return;
            }

            switch (action)
            {
                case "library":
                    if (!DownloadFlowPresentation.TryParseLibraryCallbackValue(value, out var libraryId))
                    {
                        Logger.LogWarning(
                            "Received invalid library callback value for download {DownloadId}: {LibraryValue}",
                            downloadId,
                            value);
                        await TryAnswerCallbackQuery(callbackQuery.Id, "Invalid library selection.", cancellationToken: cancellationToken);
                        callbackAnswerAttempted = true;
                        return;
                    }

                    download.TargetLibraryId = libraryId.ToString();
                    await AdvanceAfterLibrarySelectionAsync(
                        orchestrator,
                        download,
                        callbackQuery.Message!.Chat.Id,
                        callbackQuery.Message.MessageId,
                        cancellationToken);
                    break;

                case "mediatype":
                    if (download.Status != DownloadStatus.AwaitingMediaType)
                    {
                        await TryAnswerCallbackQuery(callbackQuery.Id, "This media type selection has already been processed.", cancellationToken: cancellationToken);
                        callbackAnswerAttempted = true;
                        break;
                    }

                    if (!Enum.TryParse<MediaType>(value, out var mediaType) || mediaType == MediaType.Unknown)
                    {
                        await TryAnswerCallbackQuery(callbackQuery.Id, "Invalid media type.", cancellationToken: cancellationToken);
                        callbackAnswerAttempted = true;
                        break;
                    }

                    download.MediaType = mediaType;
                    if (mediaType == MediaType.Movie)
                    {
                        download.Season = null;
                    }

                    await TryAnswerCallbackQuery(
                        callbackQuery.Id,
                        download.MediaType == MediaType.Series ? "Media type selected." : "Searching download providers…",
                        cancellationToken: cancellationToken);
                    callbackAnswerAttempted = true;

                    await AdvanceAfterMediaTypeSelectionAsync(
                        orchestrator,
                        download,
                        callbackQuery.Message!.Chat.Id,
                        callbackQuery.Message.MessageId,
                        cancellationToken);

                    break;

                case "season":
                    if (download.Status != DownloadStatus.AwaitingSeason)
                    {
                        await TryAnswerCallbackQuery(callbackQuery.Id, "This season selection has already been processed.", cancellationToken: cancellationToken);
                        callbackAnswerAttempted = true;
                        break;
                    }

                    if (!int.TryParse(value, out var seasonNum))
                    {
                        await TryAnswerCallbackQuery(callbackQuery.Id, "Invalid season.", cancellationToken: cancellationToken);
                        callbackAnswerAttempted = true;
                        break;
                    }

                    download.Season = seasonNum;
                    await TryAnswerCallbackQuery(callbackQuery.Id, "Searching download providers…", cancellationToken: cancellationToken);
                    callbackAnswerAttempted = true;
                    await BotClientWrapper.Client.EditMessageText(
                        callbackQuery.Message!.Chat.Id,
                        callbackQuery.Message.MessageId,
                        $"🔎 Searching download providers for <b>{download.Title}</b>, season {seasonNum}…",
                        ParseMode.Html,
                        cancellationToken: cancellationToken);
                    await ContinueWithAutoSearchOrPath(orchestrator, download, callbackQuery, cancellationToken);
                    break;

                case "result":
                    if (!int.TryParse(value, out var resultIndex) || download.SearchResults == null || resultIndex < 0 || resultIndex >= download.SearchResults.Length)
                    {
                        await TryAnswerCallbackQuery(callbackQuery.Id, "Invalid search result.", cancellationToken: cancellationToken);
                        callbackAnswerAttempted = true;
                        break;
                    }

                    var selectedResult = download.SearchResults[resultIndex];
                    download.LinkOrMagnet = selectedResult.DownloadLink;
                    download.SourcePassword = selectedResult.Password;
                    await TryAnswerCallbackQuery(callbackQuery.Id, "Result selected.", cancellationToken: cancellationToken);
                    callbackAnswerAttempted = true;

                    await ShowDynamicPathVariableSelection(orchestrator, download, callbackQuery, cancellationToken);
                    break;

                case "pathvar":
                {
                    if (!DownloadFlowPresentation.TryParsePathVariableSelection(download.Id, callbackQuery.Data, out var name, out var selectedValue))
                    {
                        await TryAnswerCallbackQuery(callbackQuery.Id, "Invalid path variable value.", cancellationToken: cancellationToken);
                        callbackAnswerAttempted = true;
                        break;
                    }

                    if (download.FilledPathVariables?.ContainsKey(name!) == true)
                    {
                        await TryAnswerCallbackQuery(
                            callbackQuery.Id,
                            "This path selection has already been processed.",
                            cancellationToken: cancellationToken);
                        callbackAnswerAttempted = true;
                        break;
                    }

                    download.FilledPathVariables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    download.FilledPathVariables[name!] = selectedValue!;

                    await ShowDynamicPathVariableSelection(orchestrator, download, callbackQuery, cancellationToken);
                    break;
                }

                case "accept":
                    // Accept the path and initiate download
                    await TryAnswerCallbackQuery(
                        callbackQuery.Id,
                        "Queuing download…",
                        cancellationToken: cancellationToken);
                    callbackAnswerAttempted = true;
                    download.UserConfirmedPath = download.SuggestedDestinationPath;
                    await BotClientWrapper.Client.EditMessageText(
                        callbackQuery.Message!.Chat.Id,
                        callbackQuery.Message.MessageId,
                        $"⏳ Queuing <b>{download.Title}</b> with a download service…\nPath: <code>{download.SuggestedDestinationPath}</code>",
                        ParseMode.Html,
                        cancellationToken: cancellationToken);
                    var success = await orchestrator.InitiateDownloadAsync(downloadId, cancellationToken);

                    if (BotClientWrapper.Client != null)
                    {
                        if (success)
                        {
                            await BotClientWrapper.Client.EditMessageText(
                                callbackQuery.Message!.Chat.Id,
                                callbackQuery.Message.MessageId,
                                BuildDownloadQueuedText(download, download.SuggestedDestinationPath),
                                ParseMode.Html,
                                cancellationToken: cancellationToken);
                            await orchestrator.SetTelegramMessageAsync(
                                download.Id,
                                callbackQuery.Message.MessageId,
                                callbackQuery.Message.Date.ToUniversalTime(),
                                cancellationToken);
                        }
                        else
                        {
                            await BotClientWrapper.Client.EditMessageText(
                                callbackQuery.Message!.Chat.Id,
                                callbackQuery.Message.MessageId,
                                $"❌ Failed to start download. {System.Net.WebUtility.HtmlEncode(DownloadFailureGuidance.AppendReplyOption(download.ErrorMessage ?? "No available download service."))}",
                                ParseMode.Html,
                                cancellationToken: cancellationToken);
                            await RegisterFailedDownloadReplyAsync(callbackQuery.Message.MessageId, download.Id, cancellationToken);
                        }
                    }

                    break;

                case "retry":
                    if (value != null && value.Equals("extraction", StringComparison.OrdinalIgnoreCase) &&
                        download.Status == DownloadStatus.ExtractionFailed)
                    {
                        if (BotClientWrapper.Client != null)
                        {
                            await TryAnswerCallbackQuery(
                                callbackQuery.Id,
                                "Waiting for extraction password…",
                                cancellationToken: cancellationToken);
                            callbackAnswerAttempted = true;
                            await BotClientWrapper.Client.EditMessageText(
                                callbackQuery.Message!.Chat.Id,
                                callbackQuery.Message.MessageId,
                                $"🔐 Waiting for an extraction password for <b>{download.Title}</b>…",
                                ParseMode.Html,
                                cancellationToken: cancellationToken);
                            var retryMessage = await BotClientWrapper.Client.SendMessage(
                                callbackQuery.Message!.Chat.Id,
                                "Reply with the extraction password to retry:",
                                replyMarkup: new ForceReplyMarkup { Selective = true },
                                cancellationToken: cancellationToken);
                            _pendingExtractionRetries[retryMessage.MessageId] = new PendingExtractionRetry { DownloadId = download.Id };
                        }
                    }
                    break;

                case "edit":
                    // Prompt user to reply with custom path
                    await TryAnswerCallbackQuery(
                        callbackQuery.Id,
                        "Waiting for a custom path…",
                        cancellationToken: cancellationToken);
                    callbackAnswerAttempted = true;
                    await orchestrator.UpdateDownloadStatus(downloadId, DownloadStatus.AwaitingPathConfirm);
                    if (BotClientWrapper.Client != null)
                    {
                        await BotClientWrapper.Client.EditMessageText(
                            callbackQuery.Message!.Chat.Id,
                            callbackQuery.Message.MessageId,
                            $"✏️ Waiting for a custom destination path for <b>{download.Title}</b>…",
                            ParseMode.Html,
                            cancellationToken: cancellationToken);
                        await BotClientWrapper.Client.SendMessage(
                            callbackQuery.Message!.Chat.Id,
                            "Please reply to this message with your custom path:",
                            replyMarkup: new ForceReplyMarkup(),
                            cancellationToken: cancellationToken);
                    }

                    break;

                case "edittype":
                    await TryAnswerCallbackQuery(
                        callbackQuery.Id,
                        "Choose the corrected media type.",
                        cancellationToken: cancellationToken);
                    callbackAnswerAttempted = true;
                    await orchestrator.UpdateDownloadStatus(downloadId, DownloadStatus.AwaitingMediaType);
                    await BotClientWrapper.Client.EditMessageText(
                        callbackQuery.Message!.Chat.Id,
                        callbackQuery.Message.MessageId,
                        $"Current type: <b>{download.MediaType}</b>\nChoose the correct media type:",
                        ParseMode.Html,
                        replyMarkup: CreateMediaTypeKeyboard(download.Id),
                        cancellationToken: cancellationToken);
                    break;

                case "cancel":
                    await TryAnswerCallbackQuery(
                        callbackQuery.Id,
                        "Canceling download…",
                        cancellationToken: cancellationToken);
                    callbackAnswerAttempted = true;
                    await BotClientWrapper.Client.EditMessageText(
                        callbackQuery.Message!.Chat.Id,
                        callbackQuery.Message.MessageId,
                        $"⏳ Canceling <b>{download.Title}</b>…",
                        ParseMode.Html,
                        cancellationToken: cancellationToken);
                    await orchestrator.CancelDownloadAsync(downloadId, cancellationToken);
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
                        await TryAnswerCallbackQuery(callbackQuery.Id, "Unknown action.", cancellationToken: cancellationToken);
                        callbackAnswerAttempted = true;
                    }

                    break;
            }

            if (!callbackAnswerAttempted && BotClientWrapper.Client != null)
            {
                await TryAnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing callback query for download {DownloadId}", downloadId);
            await orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.Failed, ex.Message);
            if (BotClientWrapper.Client != null && callbackQuery.Message != null)
            {
                await BotClientWrapper.Client.EditMessageText(
                    callbackQuery.Message.Chat.Id,
                    callbackQuery.Message.MessageId,
                    $"❌ {System.Net.WebUtility.HtmlEncode(DownloadFailureGuidance.AppendReplyOption(download.ErrorMessage ?? ex.Message))}",
                    ParseMode.Html,
                    cancellationToken: cancellationToken);
                await RegisterFailedDownloadReplyAsync(callbackQuery.Message.MessageId, download.Id, cancellationToken);
            }

            if (!callbackAnswerAttempted && BotClientWrapper.Client != null)
            {
                await TryAnswerCallbackQuery(callbackQuery.Id, "An error occurred.", cancellationToken: cancellationToken);
            }
        }
        finally
        {
            _activeDownloadCallbacks.TryRemove(downloadId, out _);
        }
    }

    internal static bool IsCallbackActionAllowed(string? action, DownloadStatus status)
    {
        return action switch
        {
            "library" => status == DownloadStatus.AwaitingLibrary,
            "mediatype" => status == DownloadStatus.AwaitingMediaType,
            "season" => status == DownloadStatus.AwaitingSeason,
            "result" => status == DownloadStatus.AwaitingSearchResult,
            "pathvar" => status == DownloadStatus.AwaitingPathVars,
            "accept" or "edit" or "edittype" => status == DownloadStatus.AwaitingPathConfirm,
            "retry" => status == DownloadStatus.ExtractionFailed,
            "cancel" => status is not DownloadStatus.Completed
                and not DownloadStatus.Canceled
                and not DownloadStatus.Failed,
            _ => true
        };
    }

    private async Task TryAnswerCallbackQuery(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken cancellationToken = default)
    {
        if (BotClientWrapper.Client == null)
        {
            return;
        }

        try
        {
            await BotClientWrapper.Client.AnswerCallbackQuery(
                callbackQueryId,
                text,
                showAlert,
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException ex)
        {
            Logger.LogWarning(ex, "Could not acknowledge Telegram callback query {CallbackQueryId}", callbackQueryId);
        }
    }

    private async Task AdvanceAfterLibrarySelectionAsync(
        IDownloadOrchestrator orchestrator,
        ManagedDownload download,
        long chatId,
        int messageId,
        CancellationToken cancellationToken)
    {
        await orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.AwaitingMediaType);
        if (download.MediaType == MediaType.Unknown)
        {
            await BotClientWrapper.Client!.EditMessageText(
                chatId,
                messageId,
                "TMDB could not determine the media type. Please select it:",
                replyMarkup: CreateMediaTypeKeyboard(download.Id),
                cancellationToken: cancellationToken);
            return;
        }

        await AdvanceAfterMediaTypeSelectionAsync(
            orchestrator,
            download,
            chatId,
            messageId,
            cancellationToken);
    }

    private async Task AdvanceAfterMediaTypeSelectionAsync(
        IDownloadOrchestrator orchestrator,
        ManagedDownload download,
        long chatId,
        int messageId,
        CancellationToken cancellationToken)
    {
        if (download.MediaType == MediaType.Series)
        {
            await orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.AwaitingSeason);
            await BotClientWrapper.Client!.EditMessageText(
                chatId,
                messageId,
                "Detected <b>Series</b>. Please select the season:",
                ParseMode.Html,
                replyMarkup: CreateSeasonKeyboard(download.Id),
                cancellationToken: cancellationToken);
            return;
        }

        await BotClientWrapper.Client!.EditMessageText(
            chatId,
            messageId,
            $"🔎 Searching download providers for <b>{System.Net.WebUtility.HtmlEncode(download.Title)}</b>…",
            ParseMode.Html,
            cancellationToken: cancellationToken);
        await ContinueWithAutoSearchOrPath(
            orchestrator,
            download,
            chatId,
            messageId,
            cancellationToken);
    }

    private static InlineKeyboardMarkup CreateMediaTypeKeyboard(Guid downloadId)
    {
        return new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData("Movie", $"dl_{downloadId}_mediatype_Movie"),
                InlineKeyboardButton.WithCallbackData("Series", $"dl_{downloadId}_mediatype_Series")
            ],
            [InlineKeyboardButton.WithCallbackData("Cancel", $"dl_{downloadId}_cancel")]
        ]);
    }

    private static InlineKeyboardMarkup CreateSeasonKeyboard(Guid downloadId)
    {
        var rows = new List<InlineKeyboardButton[]>();
        for (var season = 1; season <= 10; season += 2)
        {
            rows.Add([
                InlineKeyboardButton.WithCallbackData($"Season {season}", $"dl_{downloadId}_season_{season}"),
                InlineKeyboardButton.WithCallbackData($"Season {season + 1}", $"dl_{downloadId}_season_{season + 1}")
            ]);
        }

        rows.Add([InlineKeyboardButton.WithCallbackData("Cancel", $"dl_{downloadId}_cancel")]);
        return new InlineKeyboardMarkup(rows);
    }

    private async Task ShowPathConfirmation(IDownloadOrchestrator orchestrator, ManagedDownload download, CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        await ShowPathConfirmation(
            orchestrator,
            download,
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            cancellationToken);
    }

    private async Task ShowPathConfirmation(
        IDownloadOrchestrator orchestrator,
        ManagedDownload download,
        long chatId,
        int messageId,
        CancellationToken cancellationToken)
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
                    chatId,
                    messageId,
                    $"❌ {System.Net.WebUtility.HtmlEncode(DownloadFailureGuidance.AppendReplyOption(download.ErrorMessage ?? "Target library not found or path is missing."))}",
                    ParseMode.Html,
                    cancellationToken: cancellationToken);
                await RegisterFailedDownloadReplyAsync(messageId, download.Id, cancellationToken);
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
                proposedPath = await pathTemplater.ResolveTemplatePathAsync(
                    library.Path,
                    librarySettings.PathTemplate,
                    download,
                    download.FilledPathVariables ?? new Dictionary<string, string>(),
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
            [
                InlineKeyboardButton.WithCallbackData("✏️ Edit Path", $"dl_{download.Id}_edit"),
                InlineKeyboardButton.WithCallbackData("🎞 Edit Type", $"dl_{download.Id}_edittype")
            ],
            [InlineKeyboardButton.WithCallbackData("❌ Cancel", $"dl_{download.Id}_cancel")]
        ]);

        var seasonInfo = download.Season.HasValue ? $"\n<b>Season:</b> {download.Season}" : "";
        await BotClientWrapper.Client.EditMessageText(
            chatId,
            messageId,
            $"<b>Download Ready</b>\n\n" +
            $"<b>Title:</b> {download.Title} ({download.Year})\n" +
            $"<b>Type:</b> {download.MediaType}{seasonInfo}\n" +
            $"<b>Path:</b> <code>{proposedPath}</code>\n\n" +
            $"Please confirm or edit the download path:",
            ParseMode.Html,
            replyMarkup: confirmKeyboard,
            cancellationToken: cancellationToken);
    }

    private async Task ContinueWithAutoSearchOrPath(
        IDownloadOrchestrator orchestrator,
        ManagedDownload download,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        await ContinueWithAutoSearchOrPath(
            orchestrator,
            download,
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            cancellationToken);
    }

    private async Task ContinueWithAutoSearchOrPath(
        IDownloadOrchestrator orchestrator,
        ManagedDownload download,
        long chatId,
        int messageId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(download.LinkOrMagnet))
        {
            await ShowPathConfirmation(orchestrator, download, chatId, messageId, cancellationToken);
            return;
        }

        var searchOrchestrator = ServiceProvider.GetService<SearchOrchestrator>();
        if (searchOrchestrator == null)
        {
            await orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.Failed, "Search orchestrator unavailable.");
            await BotClientWrapper.Client!.EditMessageText(
                chatId,
                messageId,
                $"❌ {DownloadFailureGuidance.AppendReplyOption(download.ErrorMessage ?? "Search orchestrator unavailable.")}",
                cancellationToken: cancellationToken);
            await RegisterFailedDownloadReplyAsync(messageId, download.Id, cancellationToken);
            return;
        }

        var config = TeleJellyPlugin.Instance!.Configuration.DownloadManager;
        if (!config.Search.Enabled)
        {
            await orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.Failed, "Automated search is disabled.");
            await BotClientWrapper.Client!.EditMessageText(
                chatId,
                messageId,
                $"❌ {DownloadFailureGuidance.AppendReplyOption(download.ErrorMessage ?? "Automated search is disabled.")}",
                cancellationToken: cancellationToken);
            await RegisterFailedDownloadReplyAsync(messageId, download.Id, cancellationToken);
            return;
        }

        var librarySettings = config.LibrarySettings.FirstOrDefault(l => l.LibraryId == download.TargetLibraryId) ?? new LibrarySettings();
        var query = download.MediaType == MediaType.Series && download.Season.HasValue
            ? $"{download.Title} {download.Year} S{download.Season:00}"
            : $"{download.Title} {download.Year}";
        var titleAliases = download.AlternativeTitles;
        if (titleAliases == null || titleAliases.Length == 0)
        {
            var mediaAnalyzer = ServiceProvider.GetService<MediaAnalyzerService>();
            if (mediaAnalyzer != null)
            {
                try
                {
                    var refreshedMetadata = await mediaAnalyzer.GetMetadataFromImdbId(download.ImdbId);
                    titleAliases = refreshedMetadata.AlternativeTitles;
                    download.AlternativeTitles = titleAliases;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Could not refresh alternative titles for existing download {DownloadId}", download.Id);
                }
            }
        }

        var searchProgress = new SearchProgress();
        var searchTask = searchOrchestrator.SearchAndRankAsync(
            query,
            download.ImdbId,
            librarySettings.QualityProfile,
            maxResults: 5,
            cancellationToken,
            config.Search.EnabledServices,
            config.MaxDownloadSizeBytes,
            titleAliases is { Length: > 0 } ? titleAliases : [download.Title],
            searchProgress);

        while (!searchTask.IsCompleted)
        {
            await TryUpdateSearchProgressAsync(chatId, messageId, download.Title, searchProgress, cancellationToken);
            var progressDelay = Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            if (await Task.WhenAny(searchTask, progressDelay) == searchTask)
            {
                break;
            }
        }

        var rankedResults = await searchTask;

        if (rankedResults.Count == 0)
        {
            await orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.Failed, "No search results found.");
            await BotClientWrapper.Client!.EditMessageText(
                chatId,
                messageId,
                $"❌ {DownloadFailureGuidance.AppendReplyOption(download.ErrorMessage ?? "No search results found.")}",
                cancellationToken: cancellationToken);
            await RegisterFailedDownloadReplyAsync(messageId, download.Id, cancellationToken);
            return;
        }

        download.SearchResults = rankedResults.ToArray();
        if (DownloadFlowPresentation.ShouldAutoSelectSearchResult(rankedResults))
        {
            var selectedResult = rankedResults[0];
            download.LinkOrMagnet = selectedResult.DownloadLink;
            download.SourcePassword = selectedResult.Password;
            await ShowDynamicPathVariableSelection(orchestrator, download, chatId, messageId, cancellationToken);
            return;
        }

        await orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.AwaitingSearchResult);

        var buttons = new List<InlineKeyboardButton[]>
        {
            rankedResults
                .Select((_, index) => InlineKeyboardButton.WithCallbackData(
                    (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"dl_{download.Id}_result_{index}"))
                .ToArray()
        };
        buttons.Add([InlineKeyboardButton.WithCallbackData("Cancel", $"dl_{download.Id}_cancel")]);

        await BotClientWrapper.Client!.EditMessageText(
            chatId,
            messageId,
            DownloadFlowPresentation.BuildSearchResultsMessage(download.Title, rankedResults),
            ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken);
    }

    private async Task TryUpdateSearchProgressAsync(
        long chatId,
        int messageId,
        string title,
        SearchProgress progress,
        CancellationToken cancellationToken)
    {
        var snapshot = progress.GetSnapshot();
        var eta = snapshot.EstimatedRemaining.TotalMinutes >= 60
            ? $"{Math.Ceiling(snapshot.EstimatedRemaining.TotalHours):0}h"
            : snapshot.EstimatedRemaining.TotalSeconds >= 60
                ? $"{Math.Ceiling(snapshot.EstimatedRemaining.TotalMinutes):0}m"
                : $"{Math.Max(1, Math.Ceiling(snapshot.EstimatedRemaining.TotalSeconds)):0}s";
        var text = $"🔎 <b>{System.Net.WebUtility.HtmlEncode(title)}</b>\n" +
                   $"<b>{snapshot.Phase}:</b> {snapshot.Percent}%\n" +
                   $"Providers: {snapshot.CompletedProviders}/{snapshot.TotalProviders} · " +
                   $"query batches: {snapshot.CompletedWorkUnits}/{snapshot.TotalWorkUnits}\n" +
                   $"ETA: ~{eta}";

        try
        {
            await BotClientWrapper.Client!.EditMessageText(
                chatId,
                messageId,
                text,
                ParseMode.Html,
                cancellationToken: cancellationToken);
        }
        catch (ApiRequestException ex)
        {
            // Telegram may reject an unchanged progress edit. Search work should continue.
            Logger.LogDebug(ex, "Could not update search progress for download message {MessageId}", messageId);
        }
    }

    private async Task ShowDynamicPathVariableSelection(
        IDownloadOrchestrator orchestrator,
        ManagedDownload download,
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        await ShowDynamicPathVariableSelection(
            orchestrator,
            download,
            callbackQuery.Message!.Chat.Id,
            callbackQuery.Message.MessageId,
            cancellationToken);
    }

    private async Task ShowDynamicPathVariableSelection(
        IDownloadOrchestrator orchestrator,
        ManagedDownload download,
        long chatId,
        int messageId,
        CancellationToken cancellationToken)
    {
        var config = TeleJellyPlugin.Instance!.Configuration.DownloadManager;
        var librarySettings = config.LibrarySettings.FirstOrDefault(l => l.LibraryId == download.TargetLibraryId) ?? new LibrarySettings();
        var pathTemplater = ServiceProvider.GetService<PathTemplateService>();
        if (pathTemplater == null)
        {
            await ShowPathConfirmation(orchestrator, download, chatId, messageId, cancellationToken);
            return;
        }

        var dynamicVars = await pathTemplater.ExtractDynamicVariablesAsync(librarySettings.PathTemplate, librarySettings);
        download.PendingPathVariables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        download.FilledPathVariables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dynamicVar in dynamicVars)
        {
            if (download.PendingPathVariables.ContainsKey(dynamicVar.Name) || download.FilledPathVariables.ContainsKey(dynamicVar.Name))
            {
                continue;
            }

            var defaultOption = dynamicVar.DefaultValue ?? dynamicVar.Options.FirstOrDefault() ?? string.Empty;
            download.PendingPathVariables[dynamicVar.Name] = defaultOption;
        }

        var nextVariable = dynamicVars.FirstOrDefault(v => !download.FilledPathVariables.ContainsKey(v.Name));
        if (nextVariable == null)
        {
            await ShowPathConfirmation(orchestrator, download, chatId, messageId, cancellationToken);
            return;
        }

        await orchestrator.UpdateDownloadStatus(download.Id, DownloadStatus.AwaitingPathVars);
        var optionButtons = nextVariable.Options
            .Select(option =>
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        option,
                        $"dl_{download.Id}_pathvar_{Uri.EscapeDataString(nextVariable.Name)}_{Uri.EscapeDataString(option)}")
                })
            .ToList();
        if (!string.IsNullOrEmpty(nextVariable.DefaultValue))
        {
            optionButtons.Add(
            [
                InlineKeyboardButton.WithCallbackData(
                    $"Default ({nextVariable.DefaultValue})",
                    $"dl_{download.Id}_pathvar_{Uri.EscapeDataString(nextVariable.Name)}_{Uri.EscapeDataString(nextVariable.DefaultValue)}")
            ]);
        }

        optionButtons.Add([InlineKeyboardButton.WithCallbackData("Cancel", $"dl_{download.Id}_cancel")]);
        await BotClientWrapper.Client!.EditMessageText(
            chatId,
            messageId,
            $"Select value for path variable <b>{nextVariable.Name}</b>:",
            ParseMode.Html,
            replyMarkup: new InlineKeyboardMarkup(optionButtons),
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
        var document = message.Document;
        var fileName = document?.FileName;
        if (document == null ||
            string.IsNullOrWhiteSpace(fileName) ||
            (!fileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) &&
             !fileName.EndsWith(".dlc", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Logger.LogInformation("Received download container file upload: {FileName}", fileName);
        Debug.Assert(BotClientWrapper.Client != null, "BotClientWrapper.Client != null");

        try
        {
            // Download the file
            var file = await BotClientWrapper.Client.GetFile(document.FileId, cancellationToken);
            using var stream = new System.IO.MemoryStream();
            await BotClientWrapper.Client.DownloadFile(file.FilePath!, stream, cancellationToken);
            var bytes = stream.ToArray();

            var pendingDownloadFile = new PendingDownloadFile
            {
                FileName = fileName,
                FileBytes = bytes,
                UserId = message.From!.Id,
                ChatId = message.Chat.Id,
                UploadedAt = DateTime.UtcNow
            };

            if (message.ReplyToMessage != null && TryGetFailedDownloadReply(message, out var failedDownloadReply))
            {
                var tempPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"telejelly-{Guid.NewGuid():N}-{System.IO.Path.GetFileName(pendingDownloadFile.FileName)}");
                await System.IO.File.WriteAllBytesAsync(tempPath, pendingDownloadFile.FileBytes, cancellationToken);
                _failedDownloadReplies.TryRemove(message.ReplyToMessage.MessageId, out _);
                await HandleFailedDownloadSourceReply(
                    message,
                    message.ReplyToMessage.MessageId,
                    failedDownloadReply,
                    $"file://{tempPath}",
                    cancellationToken,
                    await ExtractDlcPasswordCandidatesAsync(fileName, bytes, cancellationToken));
                return;
            }

            if (DownloadFlowPresentation.TryParseDownloadFileCaption(message.Caption, BotInfo?.Username, out var imdbId))
            {
                await StartDownloadFileWorkflow(message, pendingDownloadFile, imdbId!, cancellationToken);
                return;
            }

            // Store temporarily until the user replies with the IMDB ID.
            _pendingDownloadFileUploads[message.MessageId] = pendingDownloadFile;

            // Prompt for IMDB ID
            await BotClientWrapper.Client.SendMessage(
                message.Chat.Id,
                "✅ Download container received! Please reply to this message with the IMDB ID (e.g., tt1234567)",
                replyMarkup: new ForceReplyMarkup { Selective = true },
                replyParameters: new ReplyParameters { MessageId = message.MessageId },
                cancellationToken: cancellationToken);

            Logger.LogInformation("Waiting for IMDB ID reply for download container: {FileName}", fileName);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process download container file upload");
            await BotClientWrapper.Client.SendMessage(
                message.Chat.Id,
                "❌ Failed to process download container file. Please try again.",
                cancellationToken: cancellationToken);
        }
    }

    private bool TryGetFailedDownloadReply(Message message, out FailedDownloadReply failedDownloadReply)
    {
        failedDownloadReply = null!;
        var repliedMessageId = message.ReplyToMessage?.MessageId;
        if (!repliedMessageId.HasValue)
        {
            return false;
        }

        if (_failedDownloadReplies.TryGetValue(repliedMessageId.Value, out failedDownloadReply!))
        {
            return true;
        }

        var orchestrator = ServiceProvider.GetService<IDownloadOrchestrator>();
        var download = orchestrator?.GetDownloadByTelegramMessage(message.Chat.Id, repliedMessageId.Value);
        if (download == null)
        {
            return false;
        }

        failedDownloadReply = new FailedDownloadReply
        {
            DownloadId = download.Id,
            RegisteredAt = download.TelegramMessageUpdatedAt ?? download.TelegramMessageCreatedAt ?? download.StartedAt
        };
        _failedDownloadReplies[repliedMessageId.Value] = failedDownloadReply;
        return true;
    }

    private async Task<string[]> ExtractDlcPasswordCandidatesAsync(
        string fileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        if (!fileName.EndsWith(".dlc", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        foreach (var service in ServiceProvider.GetServices<IHostedDownloadService>())
        {
            var password = await service.ExtractPasswordFromDlcAsync(content, cancellationToken);
            if (!string.IsNullOrWhiteSpace(password))
            {
                return [password];
            }
        }

        return [];
    }

    private async Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException => $"Telegram API Error: {apiRequestException.Message}",
            _ => exception.ToString()
        };

        Logger.LogError("Bot update handling Error: {Err}", errorMessage);

        // Telegram.Bot resumes polling as soon as this callback completes. Avoid a tight
        // retry loop for persistent runtime, network, or API failures.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown while waiting to retry.
        }
    }

    private static string BuildDownloadQueuedText(ManagedDownload download, string? path)
    {
        var title = System.Net.WebUtility.HtmlEncode(download.Title);
        var encodedPath = System.Net.WebUtility.HtmlEncode(path ?? "not selected");
        if (download.Status == DownloadStatus.Resolving)
        {
            return $"✅ DLC queued for <b>{title}</b>.\n" +
                   "JDownloader is resolving all LinkGrabber links; the download will start automatically only after that crawler job finishes.\n" +
                   $"Path: <code>{encodedPath}</code>";
        }

        return $"✅ Download started for <b>{title}</b>!\nPath: <code>{encodedPath}</code>";
    }

    private sealed class PendingDownloadFile
    {
        public string FileName { get; init; } = string.Empty;
        public byte[] FileBytes { get; init; } = [];
        public long UserId { get; init; }
        public long ChatId { get; init; }
        public DateTime UploadedAt { get; init; }
    }

    private sealed class PendingExtractionRetry
    {
        public Guid DownloadId { get; init; }
    }

    private sealed class FailedDownloadReply
    {
        public Guid DownloadId { get; init; }
        public DateTime RegisteredAt { get; init; }
    }
}
