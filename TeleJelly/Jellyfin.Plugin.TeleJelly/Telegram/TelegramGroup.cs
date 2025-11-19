using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     The configuration required for a virtual Group of Telegram users.
/// </summary>
[XmlRoot("PluginConfiguration")]
public class TelegramGroup
{
    /// <summary>
    ///     Gets or sets the unique name for the virtual group.
    /// </summary>
    [Required]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "String must be between 3 and 32 characters")]
    [RegularExpression(@"^[a-zA-Z0-9_\-]+$",
        ErrorMessage = "Only letters, numbers, underscore, and hyphen are allowed")]
    public string GroupName { get; set; } = "SampleText";

    /// <summary>
    ///     Gets or sets a value indicating whether this group has access to ALL folders.
    /// </summary>
    public bool EnableAllFolders { get; set; }

    /// <summary>
    ///     Gets or sets the folders that are allowed from the given group if not all.
    /// </summary>
    public List<string> EnabledFolders { get; set; } = new();

    /// <summary>
    ///     Gets or sets the Users that are allowed for the given group.
    /// </summary>
    public List<string> UserNames { get; set; } = new();

    /// <summary>
    ///     Gets or set the optionally linked Telegram Chat and its related settings.
    /// </summary>
    public TelegramGroupChat? TelegramGroupChat { get; set; }
}
