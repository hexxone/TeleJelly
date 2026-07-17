using System.Collections.Generic;

namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration.Library;

public class LibrarySettings
{
    public string LibraryId { get; set; } = "";
    public string LibraryName { get; set; } = "";
    public string PathTemplate { get; set; } = "{title} ({year})/{title} ({year}){ext}";
    public List<DynamicPathVariable> DynamicVariables { get; set; } = [];
    public QualityProfile QualityProfile { get; set; } = new();
}
