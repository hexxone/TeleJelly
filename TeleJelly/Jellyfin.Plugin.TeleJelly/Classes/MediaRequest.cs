using System;

namespace Jellyfin.Plugin.TeleJelly.Classes;

/// <summary>
///     DTO representing a single media request.
///     Used both by the Telegram command layer and the configuration API.
///
///     TODO remove "requested" items in NotificationService when they arrive.
/// </summary>
public class MediaRequest
{
    public Guid ItemId { get; set; }

    public string ImdbId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public string? TypeName { get; set; }

    public string? ExtraInfo { get; set; }

    public string UserId { get; set; } = "unknown";

    public string UserDisplayName { get; set; } = "Unknown";

    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
}
