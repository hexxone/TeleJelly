#region

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Telegram.Commands;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Jellyfin.Plugin.TeleJelly.Classes.Models;

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
                // Handle callback queries from inline keyboards
                case { Type: UpdateType.CallbackQuery, CallbackQuery: not null }:
                    await HandleCallbackQuery(update.CallbackQuery, cancellationToken);
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
        if (!message.Text!.StartsWith('/'))
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

    private async Task HandleCallbackQuery(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery.Data == null) return;

        Logger.LogInformation("Received callback query: {Data}", callbackQuery.Data);

        var parts = callbackQuery.Data.Split('_');
        if (parts.Length < 3 || parts[0] != "dl" || !Guid.TryParse(parts[1], out var downloadId))
        {
            await BotClientWrapper.Client.AnswerCallbackQueryAsync(callbackQuery.Id, "Invalid callback data.", cancellationToken: cancellationToken);
            return;
        }

        var orchestrator = ServiceProvider.GetService(typeof(DownloadOrchestrator)) as DownloadOrchestrator;
        if (orchestrator == null)
        {
            Logger.LogError("DownloadOrchestrator not found in service provider.");
            await BotClientWrapper.Client.AnswerCallbackQueryAsync(callbackQuery.Id, "Internal server error.", cancellationToken: cancellationToken);
            return;
        }

        var download = orchestrator.GetDownload(downloadId);
        if (download == null)
        {
            await BotClientWrapper.Client.AnswerCallbackQueryAsync(callbackQuery.Id, "Download not found.", cancellationToken: cancellationToken);
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

                    var mediaTypeKeyboard = new InlineKeyboardMarkup(new[]
                    {
                        InlineKeyboardButton.WithCallbackData("Movie", $"dl_{download.Id}_mediatype_Movie"),
                        InlineKeyboardButton.WithCallbackData("Series", $"dl_{download.Id}_mediatype_Series"),
                        InlineKeyboardButton.WithCallbackData("Cancel", $"dl_{download.Id}_cancel")
                    });

                    await BotClientWrapper.Client.EditMessageTextAsync(
                        callbackQuery.Message.Chat.Id,
                        callbackQuery.Message.MessageId,
                        "Please select the media type:",
                        replyMarkup: mediaTypeKeyboard,
                        cancellationToken: cancellationToken);
                    break;

                case "mediatype":
                    download.MediaType = Enum.Parse<MediaType>(value);
                    await orchestrator.UpdateDownloadStatus(downloadId, DownloadStatus.AwaitingPathConfirm);

                    // For simplicity, we'll auto-confirm the path for now.
                    // In a real implementation, you would ask the user to confirm or edit the path.
                    var library = _libraryManager.GetItemById(download.TargetLibraryId);
                    download.UserConfirmedPath = library.Path;
                    await orchestrator.UpdateDownloadStatus(downloadId, DownloadStatus.Downloading);

                    await BotClientWrapper.Client.EditMessageTextAsync(
                        callbackQuery.Message.Chat.Id,
                        callbackQuery.Message.MessageId,
                        $"Media type set. Download starting...",
                        cancellationToken: cancellationToken);
                    break;

                case "cancel":
                    await orchestrator.UpdateDownloadStatus(downloadId, DownloadStatus.Canceled);
                     await BotClientWrapper.Client.EditMessageTextAsync(
                        callbackQuery.Message.Chat.Id,
                        callbackQuery.Message.MessageId,
                        $"Download for <b>{download.Title}</b> has been canceled.",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Html,
                        cancellationToken: cancellationToken);
                    break;

                default:
                     await BotClientWrapper.Client.AnswerCallbackQueryAsync(callbackQuery.Id, "Unknown action.", cancellationToken: cancellationToken);
                     break;
            }
            await BotClientWrapper.Client.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing callback query for download {DownloadId}", downloadId);
            await BotClientWrapper.Client.AnswerCallbackQueryAsync(callbackQuery.Id, "An error occurred.", cancellationToken: cancellationToken);
        }
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
}
