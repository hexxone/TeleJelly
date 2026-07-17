using System;

namespace Jellyfin.Plugin.TeleJelly.Services.Download;

internal sealed class DownloadRejectedException : Exception
{
    public DownloadRejectedException(string message)
        : base(message)
    {
    }
}
