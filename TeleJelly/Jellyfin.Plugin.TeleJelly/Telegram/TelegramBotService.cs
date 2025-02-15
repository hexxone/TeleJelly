using System;
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
    private readonly ILogger _logger;
    private readonly TelegramBotClient _botClient;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly CommandBase[] _commands;

    internal readonly PluginConfiguration _config;

    private User? _botInfo;


    /// <summary>
    ///     Constructs a new instance of the BotService.
    /// </summary>
    /// <param name="botToken"></param>
    /// <param name="config"></param>
    /// <param name="logger"></param>
    /// <param name="commands"></param>
    internal TelegramBotService(string botToken, PluginConfiguration config, ILogger logger, CommandBase[] commands)
    {
        _botClient = new TelegramBotClient(botToken);
        _cancellationTokenSource = new CancellationTokenSource();
        _config = config;
        _logger = logger;
        _commands = commands;
    }

    /// <summary>
    ///     Starts polling for bot messages.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandlePollingErrorAsync,
                cancellationToken: _cancellationTokenSource.Token
            );

            _botInfo = await _botClient.GetMe();
            _logger.LogInformation("Telegram Bot listening as @{UserName}", _botInfo.Username);
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

            // Handle chat member updates
            if (update is { Type: UpdateType.ChatMember, ChatMember: not null })
            {
                await HandleChatMemberUpdate(update, cancellationToken);
            }
            // Handle commands
            else if (update is { Type: UpdateType.Message, Message.Text: not null })
            {
                await HandleBotMessage(update, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error handling update: {ErrMsg}", ex.Message);
        }
    }

    private async Task HandleChatMemberUpdate(Update update, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Bot received Update type: {Type}", update.Type);

        var member = update.ChatMember!;
        var user = member.NewChatMember.User;
        var groupId = member.Chat.Id;

        var jellyfinGroup = _config.TelegramGroups.Find(g => g.LinkedTelegramGroupId == groupId);

        if (string.IsNullOrEmpty(user.Username))
        {
            await _botClient.SendMessage(
                groupId,
                $"Warning: User {user.FirstName} {user.LastName} does not have a Telegram username set. " +
                "They need to set a username to use TeleJelly SSO login.",
                cancellationToken: cancellationToken);

            _logger.LogInformation("User Id '{UserId}' has caused a Group ChatMember event but has no Telegram username set.", user.Id);
            return;
        }

        // User added to group
        if (member.NewChatMember.Status == ChatMemberStatus.Member)
        {
            if (jellyfinGroup == null)
            {
                // TODO if self, print "welcome" message / instructions - otherwise: maybe print message "group not linked" ?

                return;
            }

            if (!jellyfinGroup.UserNames.Contains(user.Username))
            {
                jellyfinGroup.UserNames.Add(user.Username);
                await _botClient.SendMessage(
                    groupId,
                    $"Added @{user.Username} to TeleJelly whitelist",
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Added @{UserName} to TeleJelly group '{Group}'", user.Username, jellyfinGroup.GroupName);
            }
        }
        // User removed from group
        else if (member.NewChatMember.Status is ChatMemberStatus.Left or ChatMemberStatus.Kicked)
        {
            if (jellyfinGroup == null)
            {
                return;
            }

            // TODO if self, remove group linking and maybe send info to administrators?

            if (jellyfinGroup.UserNames.Remove(user.Username))
            {
                await _botClient.SendMessage(
                    groupId,
                    $"Removed @{user.Username} from TeleJelly whitelist",
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Removed @{UserName} from TeleJelly group '{Group}'", user.Username, jellyfinGroup.GroupName);
            }
        }
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
                await _botClient.SendMessage(
                    message.Chat.Id,
                    "You are not an administrator.",
                    cancellationToken: cancellationToken);
                isProcessed = true;
                break;
            }

            _logger.LogDebug("Executing command: {Command}", command.Command);
            await command.Execute(_botClient, message, cancellationToken);
            isProcessed = true;
            break;
        }

        if (!isProcessed)
        {
            await _botClient.SendMessage(message.Chat.Id, "Unknown command.", cancellationToken: cancellationToken);
        }
    }

    private static string? GetCommandText(string messageText, string botUsername)
    {
        // Strip "/" slash and get the first word as command
        var commandText = messageText[1..];

        // If contains spaces, get first word as command
        var spaceIndex = commandText.IndexOf(' ');
        if (spaceIndex > 0)
        {
            commandText = commandText[..spaceIndex];
        }

        // Handle directed bot commands (e.g. /command@botname)
        // If command is directed at a different bot, ignore it
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
    ///
    /// </summary>
    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
    }
}
