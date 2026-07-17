namespace Jellyfin.Plugin.TeleJelly.Classes.Models;

public enum DownloadStatus
{
    Pending,
    AwaitingMediaType,
    AwaitingSeason,
    AwaitingLibrary,
    AwaitingSearchResult,
    AwaitingPathVars,
    AwaitingPathConfirm,
    Resolving,
    Downloading,
    Extracting,
    ExtractionFailed,
    Analyzing,
    Organizing,
    Completed,
    Canceled,
    Failed,
    Stalled
}
