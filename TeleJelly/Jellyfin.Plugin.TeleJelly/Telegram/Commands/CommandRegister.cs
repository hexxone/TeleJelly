using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Command for checking if all admin usernames from Telegram are actually registered and if not, registering them.
/// </summary>
// ReSharper disable once UnusedType.Global
internal class CommandRegister : ICommandBase
{
    /// <summary>
    ///     Gets what command to trigger on.
    /// </summary>
    public string Command => "register";

    /// <summary>
    ///     Gets a value indicating whether this command can only be run as Admin.
    /// </summary>
    public bool NeedsAdmin => false;


    /// <summary>
    ///     The action code to trigger for the Command.
    /// </summary>
    public async Task Execute(TelegramBotService telegramBotService,
        Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var botClient = telegramBotService._client;
        if (message.Chat.Type == ChatType.Private)
        {
            await botClient.SendMessage(
                message.Chat.Id,
                isAdmin ? Constants.PrivateAdminWelcomeMessage : Constants.PrivateUserWelcomeMessage,
                cancellationToken: cancellationToken);

            return;
        }

        var linkedGroup = telegramBotService._config.TelegramGroups.FirstOrDefault(g =>
            g.TelegramGroupChat != null && g.TelegramGroupChat.TelegramChatId == message.Chat.Id);

        if (linkedGroup != null)
        {
            if (isAdmin)
            {
                var members = await botClient.GetChatAdministrators(message.Chat.Id, cancellationToken);
                var addedUsers = new List<string>();
                var existingUsers = new List<string>();
                var missingUsernames = new List<string>();

                foreach (var member in members)
                {
                    if (string.IsNullOrEmpty(member.User.Username))
                    {
                        missingUsernames.Add($"{member.User.FirstName} {member.User.LastName}");
                    }
                    else if (linkedGroup.UserNames.Contains(member.User.Username))
                    {
                        existingUsers.Add(member.User.Username);
                    }
                    else if (linkedGroup.TelegramGroupChat?.SyncUserNames ?? false)
                    {
                        linkedGroup.UserNames.Add(member.User.Username);
                        addedUsers.Add(member.User.Username);
                    }
                }

                var response = "TeleJelly Registration Report:\n";
                if (addedUsers.Any())
                {
                    TeleJellyPlugin.Instance!.SaveConfiguration(telegramBotService._config);
                    response += $"\nNow Added Users:\n{string.Join("\n", addedUsers)}\n";
                }

                if (existingUsers.Any())
                {
                    response += $"\nExisting Users:\n{string.Join("\n", existingUsers)}\n";
                }

                if (missingUsernames.Any())
                {
                    response += $"\nUsers without a username:\n{string.Join("\n", missingUsernames)}\n";
                }

                await botClient.SendMessage(
                    message.Chat.Id,
                    response,
                    cancellationToken: cancellationToken);
            }
            else
            {
                var user = message.From;
                if (user != null && !string.IsNullOrEmpty(user.Username))
                {
                    if (linkedGroup.UserNames.Contains(user.Username))
                    {
                        await botClient.SendMessage(
                            message.Chat.Id,
                            $"You are already added to the group, @{user.Username}.",
                            cancellationToken: cancellationToken);
                    }
                    else if (linkedGroup.TelegramGroupChat?.SyncUserNames ?? false)
                    {
                        var baseUrl = telegramBotService._config.LoginBaseUrl;
                        var serverUrl = baseUrl != null ? $"\nYou can now login here: {baseUrl}" : "";

                        linkedGroup.UserNames.Add(user.Username);
                        TeleJellyPlugin.Instance!.SaveConfiguration(telegramBotService._config);
                        await botClient.SendMessage(
                            message.Chat.Id,
                            $"Welcome @{user.Username}! You have been added to the TeleJelly group.{serverUrl}",
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(
                            message.Chat.Id,
                            $"Sorry @{user.Username}! Automatic Username Sync is disabled for this group.",
                            cancellationToken: cancellationToken);
                    }
                }
                else
                {
                    var userName = user != null ? $"{user.FirstName} {user.LastName}" : "user";
                    await botClient.SendMessage(
                        message.Chat.Id,
                        $"Warning: User '{userName}' does not have a Telegram username set. " +
                        "You need to set a username before using TeleJelly login.",
                        cancellationToken: cancellationToken);
                }
            }
        }
        else
        {
            await botClient.SendMessage(
                message.Chat.Id,
                Constants.GroupWelcomeMessage,
                cancellationToken: cancellationToken);
        }
    }
}
