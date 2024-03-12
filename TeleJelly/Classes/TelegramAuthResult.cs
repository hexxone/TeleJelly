namespace Jellyfin.Plugin.TeleJelly.Classes;

/// <summary>
///     Represents the result of a Telegram auth data validation.
/// </summary>
public class TelegramAuthResult
{
    /// <summary>
    ///     Gets or sets a value indicating whether data is valid.
    /// </summary>
    public bool Ok { get; set; }

    /// <summary>
    ///     Gets or sets error message if Data is invalid.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
