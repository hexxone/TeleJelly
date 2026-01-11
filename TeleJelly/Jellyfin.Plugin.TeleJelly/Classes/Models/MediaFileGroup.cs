using System.Collections.Generic;

namespace Jellyfin.Plugin.TeleJelly.Classes.Models;

public class MediaFileGroup
{
    public AnalyzedFile? VideoFile { get; set; }
    public List<AnalyzedFile> SubtitleFiles { get; set; } = [];
    public List<AnalyzedFile> AudioFiles { get; set; } = new();
    public List<AnalyzedFile> OtherFiles { get; set; } = new();
}

public class AnalyzedFile
{
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public AnalyzedFileType FileType { get; set; }
}

public enum AnalyzedFileType
{
    Video,
    Audio,
    Subtitle,
    Archive,
    Other
}
