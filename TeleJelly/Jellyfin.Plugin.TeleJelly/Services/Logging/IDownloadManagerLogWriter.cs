using System;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services.Logging;

internal interface IDownloadManagerLogWriter
{
    void Write(LogLevel level, string source, string message, Exception? exception = null);
}