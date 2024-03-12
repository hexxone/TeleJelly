using System.ComponentModel.DataAnnotations;

namespace Jellyfin.Plugin.TeleJelly.Classes;

/// <summary>
///     TODO.
/// </summary>
public class ValidateBotTokenRequest
{
    /// <summary>
    ///     Gets or sets tODO.
    /// </summary>
    [Required]
    public string Token { get; set; } = default!;
}
