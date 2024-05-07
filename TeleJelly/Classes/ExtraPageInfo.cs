using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TeleJelly.Classes;

/// <summary>
///     PluginPageInfo with extra property "NeedsReplacement".
/// </summary>
public class ExtraPageInfo : PluginPageInfo
{
    /// <summary>
    ///     Whether the file needs to have strings like {{SERVER_URL}} replaced.
    /// </summary>
    public bool NeedsReplacement { get; set; }
}
