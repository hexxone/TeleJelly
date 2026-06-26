using System.Collections.Generic;

namespace Jellyfin.Plugin.TeleJelly.Services.Logging;

public interface IDownloadManagerLogStore
{
    IReadOnlyList<DownloadManagerLogEntry> GetRecent(int limit = 200);
}