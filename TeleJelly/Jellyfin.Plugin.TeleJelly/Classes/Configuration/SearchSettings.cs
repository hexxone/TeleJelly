using System.Collections.Generic;

namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration;

public class SearchSettings
{
    public bool Enabled { get; set; } = false;
    public List<string> EnabledServices { get; set; } = [];
}