namespace Jellyfin.Plugin.TeleJelly.Classes;

/// <summary>
///     Bot-Token validation response.
/// </summary>
public class ValidateBotTokenResponse
{
    /// <summary>
    ///     Gets or sets a value indicating whether Token was valid.
    /// </summary>
    public bool Ok { get; set; }

    /// <summary>
    ///     Gets or sets Error Message if Result is NOT ok.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets or sets Username if Result IS ok.
    /// </summary>
    public string? BotUsername { get; set; }
}