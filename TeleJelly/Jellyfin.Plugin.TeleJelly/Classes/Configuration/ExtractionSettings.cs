using System.Collections.Generic;

namespace Jellyfin.Plugin.TeleJelly.Classes.Configuration;

public class ExtractionSettings
{
    public bool Enabled { get; set; } = true;
    public List<string> Passwords { get; set; } = ["password", "123456"];
    public bool ExtractPasswordsFromDlc { get; set; } = true;
    public bool NotifyOnFailure { get; set; } = true;
    public bool DeleteArchivesAfterExtraction { get; set; } = false;
    public int RecursiveExtractionDepth { get; set; } = 0;
    public int FreeSpaceMarginPercent { get; set; } = 20;
}