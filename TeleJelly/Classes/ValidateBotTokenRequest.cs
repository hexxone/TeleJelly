#region

using System.ComponentModel.DataAnnotations;

#endregion

namespace Jellyfin.Plugin.TeleJelly.Classes;

/// <summary>
///     Validate Telegram Bot Token Request.
/// </summary>
public class ValidateBotTokenRequest
{
    /// <summary>
    ///     Gets or sets Token.
    /// </summary>
    [Required]
    [StringLength(256)]
    public string Token { get; set; } = default!;
}
