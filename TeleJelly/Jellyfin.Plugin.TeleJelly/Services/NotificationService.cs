using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text;
using Jellyfin.Plugin.TeleJelly.Classes;
using MediaBrowser.Model.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Services;

/// <summary>
///     Provides functionality to handle notifications related to item updates and additions.
///     This service monitors changes to library items, checks the completeness of metadata,
///     and sends notifications using a Telegram bot integration. It also ensures that
///     notifications are managed effectively, including handling pending notifications and
///     timing out incomplete notifications.
/// </summary>
public class NotificationService : IDisposable
{
    private readonly ILogger<NotificationService> _logger;
    private readonly TelegramBotClientWrapper _botClientWrapper;
    private readonly IConfigurationManager _configurationManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<Guid, DateTime> _pendingNotifications = new();
    private readonly Timer _timer;

    /// <summary>
    ///     Provides functionality to handle notifications related to item updates and additions.
    ///     This class is responsible for managing notifications, checking metadata completeness, and
    ///     handling timed-out notifications.
    /// </summary>
    public NotificationService(ILogger<NotificationService> logger, TelegramBotClientWrapper botClientWrapper, IConfigurationManager configurationManager, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _botClientWrapper = botClientWrapper;
        _configurationManager = configurationManager;
        _serviceProvider = serviceProvider;
        _timer = new Timer(CheckForTimeouts, null, TimeSpan.Zero, TimeSpan.FromHours(1));
    }

    /// <summary>
    ///     Handles logic triggered when a library item's metadata or properties are updated.
    ///     This method processes updates to ensure notifications are sent if the metadata
    ///     is complete and removes associated pending notifications if applicable.
    /// </summary>
    /// <param name="sender">The source of the event, typically the library manager.</param>
    /// <param name="e">The event data containing details about the updated item.</param>
    public void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        _logger.LogInformation("Item updated: {ItemName}", e.Item.Name);

        if (IsMetadataComplete(e.Item) && _pendingNotifications.TryRemove(e.Item.Id, out _))
        {
            SendRichNotificationAsync(e.Item);
        }
    }

    /// <summary>
    ///     Handles the event when a new item is added to the library. This method checks the item's metadata
    ///     for completeness and, if complete, sends a notification using a Telegram bot integration.
    ///     If the metadata is incomplete, the item is added to a collection of pending notifications for further processing.
    /// </summary>
    /// <param name="sender">
    ///     The source of the event, typically the library manager that triggered the item addition event.
    /// </param>
    /// <param name="e">
    ///     An instance of <see cref="ItemChangeEventArgs"/> that contains information about the item
    ///     that was added, including its metadata and identifier.
    /// </param>
    public void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        _logger.LogInformation("Item added: {ItemName}", e.Item.Name);

        if (IsMetadataComplete(e.Item))
        {
            SendRichNotificationAsync(e.Item);
        }
        else
        {
            _pendingNotifications.TryAdd(e.Item.Id, DateTime.UtcNow);
        }
    }

    private void CheckForTimeouts(object? state)
    {
        _logger.LogInformation("Checking for timed out notifications");

        var libraryManager = _serviceProvider.GetRequiredService<ILibraryManager>();

        foreach (var item in _pendingNotifications)
        {
            if (DateTime.UtcNow - item.Value <= TimeSpan.FromHours(24))
            {
                continue;
            }

            if (!_pendingNotifications.TryRemove(item.Key, out _))
            {
                continue;
            }

            var baseItem = libraryManager.GetItemById(item.Key);
            if (baseItem != null)
            {
                SendRichNotificationAsync(baseItem, true);
            }
        }
    }

    private bool IsMetadataComplete(BaseItem item)
    {
        return !string.IsNullOrEmpty(item.GetProviderId(MetadataProvider.Imdb)) &&
               item.HasImage(ImageType.Primary);
    }

    private void SendRichNotificationAsync(BaseItem item, bool isTimeout = false)
    {
        if (_botClientWrapper.Client == null)
        {
            _logger.LogInformation("Cannot send notification for '{ItemName}' because Bot-Client is null.", item.Name);
            return;
        }

        var config = TeleJellyPlugin.Instance?.Configuration
                     ?? throw new Exception("TeleJellyPlugin Instance/Config was null.");

        var notifyGroups = config.TelegramGroups
            .Where(g => g.TelegramGroupChat is { NotifyNewContent: true })
            .ToArray();

        if (notifyGroups.Length == 0)
        {
            _logger.LogInformation("Cannot send notification for '{ItemName}' because no group has notifications enabled.", item.Name);
            return;
        }

        _logger.LogInformation("Sending rich notification for '{ItemName}' to {Amount} groups.", item.Name, notifyGroups.Length);

        var imageUrl = item.HasImage(ImageType.Primary)
            ? item.GetImagePath(ImageType.Primary)
            : item.GetImagePath(ImageType.Backdrop);

        var message = new StringBuilder();
        if (isTimeout)
        {
            message.AppendLine("New content added (metadata might be incomplete):");
        }
        else
        {
            message.AppendLine("New content added:");
        }

        // Build the display text (name + year + type)
        var baseUrl = config.LoginBaseUrl;
        var displayText = item.GetDisplayText();

        // Make it a link if baseUrl is available
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            var itemUrl = $"{baseUrl.TrimEnd('/')}/web/index.html#!/details?id={item.Id:N}";
            message.Append('[').Append(displayText).Append("](").Append(itemUrl).Append(')');
        }
        else
        {
            message.Append(displayText);
        }

        var extraLink = item.GetExtraLink();
        if (extraLink != null)
        {
            message.Append(extraLink);
        }

        message.AppendLine();


        var audioLanguages = item.GetMediaStreams()
            .Where(m => m.Type == MediaStreamType.Audio)
            .Select(m => m.Language)
            .Distinct().ToArray();

        if (audioLanguages.Length > 0)
        {
            message.AppendLine("Audio: " + string.Join(", ", audioLanguages));
        }

        var subtitleLanguages = item.GetMediaStreams()
            .Where(m => m.Type == MediaStreamType.Subtitle)
            .Select(m => m.Language)
            .Distinct().ToArray();

        if (subtitleLanguages.Length > 0)
        {
            message.AppendLine($"Subtitles: " + string.Join(", ", subtitleLanguages));
        }

        foreach (var notifyGroup in notifyGroups)
        {
            try
            {
                using var fromFile = new FileStream(imageUrl, FileMode.Open, FileAccess.Read);

                _ = _botClientWrapper.Client.SendPhoto(
                    chatId: notifyGroup.TelegramGroupChat!.TelegramChatId,
                    showCaptionAboveMedia: true,
                    caption: message.ToString(),
                    photo: InputFile.FromStream(fromFile)
                ).Wait(TimeSpan.FromSeconds(30));
            }
            catch (Exception e)
            {
                _logger.LogInformation("Failed to send notification for '{ItemName}' to group '{Group}'", item.Name, notifyGroup.GroupName);
                Console.WriteLine(e);
                throw;
            }
        }
    }

    /// <summary>
    ///     Releases all resources used by the <see cref="NotificationService" />.
    ///     This method disposes of the internal timer and performs any necessary cleanup tasks.
    /// </summary>
    public void Dispose()
    {
        _timer.Dispose();
    }
}
