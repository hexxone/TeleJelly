using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///
/// </summary>
public class TelegramBotService : IDisposable
{
    private readonly ILogger _logger;
    private readonly TelegramBotClient _botClient;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly PluginConfiguration _config;

    /// <summary>
    ///
    /// </summary>
    /// <param name="botToken"></param>
    /// <param name="config"></param>
    /// <param name="logger"></param>
    public TelegramBotService(string botToken, PluginConfiguration config, ILogger logger)
    {
        _botClient = new TelegramBotClient(botToken);
        _cancellationTokenSource = new CancellationTokenSource();
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        try
        {
            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandlePollingErrorAsync,
                cancellationToken: _cancellationTokenSource.Token
            );

            var me = await _botClient.GetMe();
            _logger.LogInformation("Telegram Bot listening as @{UserName}", me.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to Start Telegram Bot: {Msg}",  ex.Message);
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        try
        {
            // Handle chat member updates
            if (update is { Type: UpdateType.ChatMember, ChatMember: not null })
            {
                _logger.LogDebug("Bot received Update type: {Type}", update.Type);

                var member = update.ChatMember;
                var user = member.NewChatMember.User;
                var groupId = member.Chat.Id;

                var jellyfinGroup = _config.TelegramGroups.Find(g => g.LinkedTelegramGroupId == groupId);
                if (jellyfinGroup == null) return;

                if (string.IsNullOrEmpty(user.Username))
                {
                    await botClient.SendMessage(
                        chatId: groupId,
                        text: $"Warning: User {user.FirstName} {user.LastName} does not have a Telegram username set. " +
                              "They need to set a username to use TeleJelly SSO login.",
                        cancellationToken: cancellationToken);

                    _logger.LogInformation("User Id '{UserId}' has caused a Group ChatMember event but has no Telegram username set.", user.Id);
                    return;
                }

                // User added to group
                if (member.NewChatMember.Status == ChatMemberStatus.Member)
                {
                    if (!jellyfinGroup.UserNames.Contains(user.Username))
                    {
                        jellyfinGroup.UserNames.Add(user.Username);
                        await botClient.SendMessage(
                            chatId: groupId,
                            text: $"Added @{user.Username} to TeleJelly whitelist",
                            cancellationToken: cancellationToken);

                        _logger.LogInformation("Added @{UserName} to TeleJelly group '{Group}'", user.Username, jellyfinGroup.GroupName);
                    }
                }
                // User removed from group
                else if (member.NewChatMember.Status == ChatMemberStatus.Left ||
                         member.NewChatMember.Status == ChatMemberStatus.Kicked)
                {
                    if (jellyfinGroup.UserNames.Remove(user.Username))
                    {
                        await botClient.SendMessage(
                            chatId: groupId,
                            text: $"Removed @{user.Username} from TeleJelly whitelist",
                            cancellationToken: cancellationToken);

                        _logger.LogInformation("Removed @{UserName} from TeleJelly group '{Group}'", user.Username, jellyfinGroup.GroupName);
                    }
                }
            }
            // Handle commands
            else if (update is { Type: UpdateType.Message, Message.Text: not null })
            {
                _logger.LogDebug("Bot received Update type: {UpdateType} from UserId: '{FromId}' text: '{MsgText}'", update.Type, update.Message.From?.Id, update.Message.Text);

                var message = update.Message;
                var isAdmin = message.From?.Username != null && _config.AdminUserNames.Contains(message.From.Username);

                if (!isAdmin)
                {
                    await botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: "You are not an administrator.",
                        cancellationToken: cancellationToken);
                    return;
                }

                if (message.Text.StartsWith("/link"))
                {
                    var parts = message.Text.Split(' ');
                    if (parts.Length != 2)
                    {
                        await botClient.SendMessage(
                            chatId: message.Chat.Id,
                            text: "Usage: /link <telejelly_group_name>",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    var groupName = parts[1];
                    var group = _config.TelegramGroups.Find(g => g.GroupName == groupName);

                    if (group == null)
                    {
                        await botClient.SendMessage(
                            chatId: message.Chat.Id,
                            text: $"TeleJelly group '{groupName}' not found",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    group.LinkedTelegramGroupId = message.Chat.Id;
                    await botClient.SendMessage(
                        chatId: message.Chat.Id,
                        text: $"Linked this Telegram group to TeleJelly group '{groupName}'",
                        cancellationToken: cancellationToken);
                }
                else if (message.Text == "/unlink")
                {
                    var group = _config.TelegramGroups.Find(g => g.LinkedTelegramGroupId == message.Chat.Id);
                    if (group != null)
                    {
                        group.LinkedTelegramGroupId = null;
                        await botClient.SendMessage(
                            chatId: message.Chat.Id,
                            text: $"Unlinked this Telegram group from TeleJelly group '{group.GroupName}'",
                            cancellationToken: cancellationToken);
                    }
                }
                else if (message.Text == "/check_usernames")
                {
                    var members = await botClient.GetChatAdministrators(message.Chat.Id, cancellationToken);
                    var missingUsernames = members
                        .Where(m => string.IsNullOrEmpty(m.User.Username))
                        .Select(m => $"{m.User.FirstName} {m.User.LastName}")
                        .ToArray();

                    if (missingUsernames.Any())
                    {
                        await botClient.SendMessage(
                            chatId: message.Chat.Id,
                            text: "The following members need to set a username to use TeleJelly SSO:\n" +
                                  string.Join("\n", missingUsernames),
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(
                            chatId: message.Chat.Id,
                            text: "All members have usernames set",
                            cancellationToken: cancellationToken);
                    }
                }
                else if (message.Text == "/whitelist")
                {
                    var group = _config.TelegramGroups.Find(g => g.LinkedTelegramGroupId == message.Chat.Id);
                    if (group != null)
                    {
                        var users = string.Join("\n", group.UserNames.Select(u => $"@{u}"));
                        await botClient.SendMessage(
                            chatId: message.Chat.Id,
                            text: $"Users in whitelist for group '{group.GroupName}':\n{users}",
                            cancellationToken: cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error handling update: {ErrMsg}", ex.Message);
        }
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
