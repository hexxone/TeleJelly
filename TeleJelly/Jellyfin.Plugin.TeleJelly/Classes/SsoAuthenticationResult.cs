using MediaBrowser.Controller.Authentication;

namespace Jellyfin.Plugin.TeleJelly.Classes;

/// <summary>
///     Represents the result of a Telegram SSO attempt.
/// </summary>
public class SsoAuthenticationResult
{
    /// <summary>
    ///     Gets or sets a value indicating whether login was successful.
    /// </summary>
    public bool Ok { get; set; }

    /// <summary>
    ///     Gets or sets contains the Jellyfin Server Address.
    /// </summary>
    public string ServerAddress { get; set; } = default!;

    /// <summary>
    ///     Gets or sets error message if Data is invalid.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    ///     Gets or sets the real JellyFin Authentication result if valid.
    /// </summary>
    public AuthenticationResult? AuthenticatedUser { get; set; }
}