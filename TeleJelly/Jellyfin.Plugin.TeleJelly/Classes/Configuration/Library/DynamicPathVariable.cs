using System.Collections.Generic;

namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration.Library;

public class DynamicPathVariable
{
    public string Name { get; set; } = "";
    public List<string> Options { get; set; } = [];
    public string? DefaultValue { get; set; }
}
