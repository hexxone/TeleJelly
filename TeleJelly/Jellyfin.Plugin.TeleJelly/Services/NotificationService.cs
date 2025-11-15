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
using MediaBrowser.Model.Entities;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Services;

/// <summary>
///
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
    ///
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
    ///
    /// </summary>
    public void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        _logger.LogInformation("Item updated: {ItemName}", e.Item.Name);

        if (IsMetadataComplete(e.Item) && _pendingNotifications.TryRemove(e.Item.Id, out _))
        {
            SendRichNotificationAsync(e.Item);
        }
    }

    /// <summary>
    ///
    /// </summary>
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

    private async void CheckForTimeouts(object state)
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

        var config = _configurationManager.GetConfiguration<PluginConfiguration>("TeleJelly");
        var notifyGroups = config.TelegramGroups
            .Where(g => g.TelegramGroupChat is { NotifyNewContent: true })
            .ToArray();

        if (_botClientWrapper.Client == null)
        {
            _logger.LogInformation("Cannot send notification for '{ItemName}' because no group has them enabled.", item.Name);
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

        message.AppendLine(item.Name);

        if (item.ProductionYear.HasValue)
        {
            message.AppendLine("Year: " + item.ProductionYear.Value);
        }

        var imdbId = item.GetProviderId(MetadataProvider.Imdb);
        if (!string.IsNullOrEmpty(imdbId))
        {
            message.AppendLine("IMDb: https://www.imdb.com/title/" + imdbId);
        }

        var audioLanguages = item.GetMediaStreams()
            .Where(m => m.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio)
            .Select(m => m.Language)
            .Distinct().ToArray();

        if (audioLanguages.Length > 0)
        {
            message.AppendLine("Audio: " + string.Join(", ", audioLanguages));
        }

        var subtitleLanguages = item.GetMediaStreams()
            .Where(m => m.Type == MediaBrowser.Model.Entities.MediaStreamType.Subtitle)
            .Select(m => m.Language)
            .Distinct().ToArray();

        if (subtitleLanguages.Length > 0)
        {
            message.AppendLine($"Subtitles: " + string.Join(", ", subtitleLanguages));
        }

        foreach (var group in config.TelegramGroups)
        {
            if (group.TelegramGroupChat is not { NotifyNewContent: true })
            {
                continue;
            }

            try
            {
                using var fromFile = new FileStream(imageUrl, FileMode.Open, FileAccess.Read);

                _ = _botClientWrapper.Client.SendPhoto(
                    chatId: group.TelegramGroupChat.TelegramChatId,
                    photo: InputFile.FromStream(fromFile),
                    caption: message.ToString()
                ).Wait(TimeSpan.FromSeconds(30));
            }
            catch (Exception e)
            {
                _logger.LogInformation("Failed to send notification for '{ItemName}' to group '{Group}'", item.Name, group.GroupName);
                Console.WriteLine(e);
                throw;
            }
        }
    }

    /// <summary>
    ///
    /// </summary>
    public void Dispose()
    {
        _timer.Dispose();
    }
}
