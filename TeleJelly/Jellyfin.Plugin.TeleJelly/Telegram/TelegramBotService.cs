using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Telegram.Commands;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     The TeleJelly Telegram Bot service which runs in the background and listens for events and commands.
///     Should get re-initialized when the botToken changes.
/// </summary>
public class TelegramBotService : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ICommandBase[] _commands;

    private User? _botInfo;

    internal readonly ILogger _logger;
    internal readonly ITelegramBotClient _client;
    internal readonly IServiceProvider _serviceProvider;

    internal PluginConfiguration _config;
    internal DateTime? _startTime;


    /// <summary>
    ///     Constructs a new instance of the BotService.
    /// </summary>
    internal TelegramBotService(IServiceProvider serviceProvider, ILogger logger,
        ICommandBase[] commands, string botToken, PluginConfiguration config)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _commands = commands;

        _logger = logger;
        _client = new TelegramBotClient(botToken);
        _serviceProvider = serviceProvider;

        _config = config;

        logger.LogInformation("{PluginName} Service: {ServiceName} initialized.", nameof(TeleJellyPlugin), nameof(TelegramBotService));
    }

    /// <summary>
    ///     Needs to be called manually on Config-Change, because the original object reference doesn't get updated.
    ///     Not sure if we could use something like IOptionsMonitor instead ?
    /// </summary>
    /// <param name="configuration"></param>
    public void UpdateConfig(PluginConfiguration configuration)
    {
        _config = configuration;
    }

    /// <summary>
    ///     Starts polling for bot messages.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            _client.StartReceiving(
                HandleUpdateAsync,
                HandlePollingErrorAsync,
                cancellationToken: _cancellationTokenSource.Token
            );

            _botInfo = await _client.GetMe();
            _logger.LogInformation("Telegram Bot listening as @{UserName}", _botInfo.Username);
            _startTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to Start Telegram Bot: {Msg}", ex.Message);
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (_botInfo == null)
            {
                throw new Exception($"No bot info available in: {nameof(TelegramBotService)}.{nameof(HandleUpdateAsync)}");
            }

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
                        TeleJellyPlugin.Instance!.SaveConfiguration(_config);
                    }

                    break;
                }
                // Handle commands
                case { Type: UpdateType.Message, Message.Text: not null }:
                    await HandleBotMessage(update, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error handling update: {ErrMsg}", ex.Message);
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
        _logger.LogDebug("Bot received Update type: {Type}", update.Type);

        var member = update.ChatMember!;
        var user = member.NewChatMember.User;
        var groupId = member.Chat.Id;

        var telegramGroup = _config.TelegramGroups.FirstOrDefault(g => g.TelegramGroupChat?.TelegramChatId == groupId);

        if (string.IsNullOrEmpty(user.Username))
        {
            await _client.SendMessage(
                groupId,
                $"Warning: User '{user.FirstName} {user.LastName}' does not have a Telegram username set. " +
                "They need to set a username before using TeleJelly login.",
                cancellationToken: cancellationToken);

            _logger.LogInformation("User Id '{UserId}' has caused a Group ChatMember event but has no Telegram username set.", user.Id);
            return false;
        }

        // User added to group
        if (member.NewChatMember.Status == ChatMemberStatus.Member)
        {
            if (telegramGroup == null)
            {
                if (user.Id == _botInfo?.Id)
                {
                    await _client.SendMessage(
                        groupId,
                        Constants.GroupWelcomeMessage,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await _client.SendMessage(
                        groupId,
                        "This group is not linked to Jellyfin. Please ask an administrator to link this group using the `/link` command.",
                        cancellationToken: cancellationToken);
                }

                return false;
            }

            if (telegramGroup.TelegramGroupChat!.SyncUserNames && !telegramGroup.UserNames.Contains(user.Username))
            {
                // add Jellyfin Public-Url to Msg if set
                var baseUrl = _config.LoginBaseUrl;
                var serverUrl = baseUrl != null ? $"\nServer URL: {baseUrl}" : "";

                telegramGroup.UserNames.Add(user.Username);
                await _client.SendMessage(
                    groupId,
                    $"Welcome @{user.Username}! You have been added to the TeleJelly whitelist. {serverUrl}",
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Added @{UserName} to TeleJelly group '{Group}'", user.Username, telegramGroup.GroupName);

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

            if (user.Id == _botInfo?.Id)
            {
                _config.TelegramGroups.Remove(telegramGroup);
                var adminMentions = string.Join(" ", _config.AdminUserNames.Select(admin => $"@{admin}"));
                var message = $"The bot has been removed from the group '{telegramGroup.GroupName}' and the link has been removed.\n\n{adminMentions}";
                await _client.SendMessage(
                    groupId,
                    message,
                    cancellationToken: cancellationToken);
                return true;
            }

            if (telegramGroup.TelegramGroupChat!.SyncUserNames && telegramGroup.UserNames.Remove(user.Username))
            {
                await _client.SendMessage(
                    groupId,
                    $"Removed @{user.Username} from TeleJelly whitelist",
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Removed @{UserName} from TeleJelly group '{Group}'", user.Username, telegramGroup.GroupName);

                return true;
            }
        }

        return false;
    }

    private async Task HandleBotMessage(Update update, CancellationToken cancellationToken)
    {
        if (_botInfo?.Username == null)
        {
            throw new Exception($"No bot info available in: {nameof(TelegramBotService)}.{nameof(HandleBotMessage)}");
        }

        var message = update.Message!;
        if (!message.Text!.StartsWith('/'))
        {
            return; // Not a command, ignore
        }

        _logger.LogDebug("Bot received Update type: {UpdateType} from UserId: '{FromId}' text: '{MsgText}'", update.Type, message.From?.Id, message.Text);

        var commandText = GetCommandText(message.Text, _botInfo.Username);
        if (commandText == null)
        {
            return; // directed at different bot
        }

        // Find & Execute Bot command
        await FindAndExecuteCommand(message, commandText, cancellationToken);
    }

    private async Task FindAndExecuteCommand(Message message, string commandText, CancellationToken cancellationToken)
    {
        try
        {
            var isAdmin = message.From?.Username != null && _config.AdminUserNames.Contains(message.From.Username);
            var isProcessed = false;
            foreach (var command in _commands)
            {
                if (!command.Command.Equals(commandText, StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                if (command.NeedsAdmin && !isAdmin)
                {
                    await _client.SendMessage(
                        message.Chat.Id,
                        "You are not an administrator.",
                        cancellationToken: cancellationToken);
                    isProcessed = true;
                    break;
                }

                _logger.LogDebug("Executing command: {Command}", command.Command);
                await command.Execute(this, message, isAdmin, cancellationToken);
                isProcessed = true;
                break;
            }

            if (!isProcessed)
            {
                await _client.SendMessage(message.Chat.Id, "Unknown command.", cancellationToken: cancellationToken);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An error occured while executing command : {Command}", commandText);
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

        _logger.LogError("Bot update handling Error: {Err}", errorMessage);

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Game-End the bot.
    /// </summary>
    public void Dispose()
    {
        _startTime = null;
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }
}
