using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Command for printing the server infos.
/// </summary>
// ReSharper disable once UnusedType.Global
internal class CommandStats : ICommandBase
{
    /// <summary>
    ///     Gets what command to trigger on.
    /// </summary>
    public string Command => "stats";

    /// <summary>
    ///     Gets a value indicating whether this command can only be run as Admin.
    /// </summary>
    public bool NeedsAdmin => true;

    /// <summary>
    ///     The action code to trigger for the Command.
    /// </summary>
    public async Task Execute(TelegramBotService telegramBotService, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        var botClient = telegramBotService._client;

        var statsMessage = GetSystemStatsMessage(telegramBotService, isAdmin);

        await botClient.SendMessage(
            message.Chat.Id,
            statsMessage,
            ParseMode.MarkdownV2,
            linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true },
            cancellationToken: cancellationToken);
    }

    private string GetSystemStatsMessage(TelegramBotService telegramBotService, bool isAdmin)
    {
        var serverApplicationHost = telegramBotService._serviceProvider.GetRequiredService<IServerApplicationHost>();
        var process = Process.GetCurrentProcess();

        // Calculate bot uptime - handle nullable TimeSpan
        string botUptimeText;
        if (telegramBotService._startTime.HasValue)
        {
            var botUptime = DateTime.Now - telegramBotService._startTime.Value;
            botUptimeText = FormatTimeSpan(botUptime);
        }
        else
        {
            botUptimeText = "Unknown";
        }

        // Get server uptime from process
        var serverUptime = DateTime.Now - process.StartTime;

        // Get system memory info
        var workingSet = process.WorkingSet64;
        var totalPhysicalMemory = GetTotalPhysicalMemory();
        var percentUsed = totalPhysicalMemory > 0
            ? (double)workingSet / totalPhysicalMemory
            : 0;

        // add Jellyfin Public-Url to Msg if set
        var baseUrl = telegramBotService._config.LoginBaseUrl;
        var serverUrl = baseUrl != null
            ? EscapeMarkdownV2("Server URL: " + baseUrl) + "\n"
            : "";

        var sb = new StringBuilder();

        sb.AppendLine(EscapeMarkdownV2("📊 TeleJelly Stats 📊"));
        sb.AppendLine();
        sb.AppendLine(EscapeMarkdownV2("🖥️ Jellyfin Server"));
        sb.Append(serverUrl);
        sb.Append(EscapeMarkdownV2("Version: ")).Append('`').Append(serverApplicationHost.ApplicationVersion).Append('`').Append('\n');
        sb.Append(EscapeMarkdownV2("Uptime: ")).Append('`').Append(FormatTimeSpan(serverUptime)).Append('`').Append('\n');

        if (isAdmin)
        {
            sb.Append(EscapeMarkdownV2("Process Memory: ")).Append('`').Append(FormatBytes(workingSet)).Append('`').Append('\n');
            if (totalPhysicalMemory > 0)
            {
                sb.Append(EscapeMarkdownV2("System Memory: ")).Append('`').Append(FormatBytes(totalPhysicalMemory)).Append('`').Append('\n');
                sb.Append(EscapeMarkdownV2("Memory Usage: ")).Append('`').Append(percentUsed.ToString("P1", CultureInfo.CurrentCulture)).Append('`').Append("\n\n");
            }
            else
            {
                sb.Append(EscapeMarkdownV2("System Memory: `Unknown`\n\n"));
            }
        }
        else
        {
            sb.AppendLine();
        }

        sb.AppendLine(EscapeMarkdownV2("🤖 Telegram Bot"));
        sb.Append(EscapeMarkdownV2("Uptime: ")).Append('`').Append(botUptimeText).Append('`').Append("\n\n");

        if (isAdmin)
        {
            sb.AppendLine(EscapeMarkdownV2("💾 Disk Space"));
            sb.Append(GetDiskInfo());
        }

        return sb.ToString();
    }

    private long GetTotalPhysicalMemory()
    {
        try
        {
            // This is a cross-platform alternative to get an estimate of total memory
            var gcInfo = GC.GetGCMemoryInfo();
            return gcInfo.TotalAvailableMemoryBytes;
        }
        catch
        {
            return 0; // Fallback if not available
        }
    }

    private string FormatTimeSpan(TimeSpan timeSpan)
    {
        return timeSpan.Days > 0
            ? $"{timeSpan.Days}d {timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s"
            : $"{timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
    }

    private string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var counter = 0;
        double number = bytes;
        while (number >= 1024 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }

        return $"{number:F2} {suffixes[counter]}";
    }

    private string GetDiskInfo()
    {
        var result = new StringBuilder();

        // Get all ready drives which have size > 0
        var drives = DriveInfo.GetDrives()
            .Where(d => d is { IsReady: true, TotalSize: > 0, DriveType: DriveType.Removable or DriveType.Fixed or DriveType.Network })
            .ToList();

        foreach (var drive in drives)
        {
            var totalSize = drive.TotalSize;
            var freeSpace = drive.AvailableFreeSpace;
            var usedSpace = totalSize - freeSpace;
            var percentUsed = (double)usedSpace / totalSize;

            // Escape text parts, keep values in backticks
            result.Append('`')
                .Append(drive.Name)
                .Append('`')
                .Append(EscapeMarkdownV2(" - "))
                .Append('`')
                .Append(FormatBytes(usedSpace))
                .Append('`')
                .Append(EscapeMarkdownV2("/"))
                .Append('`')
                .Append(FormatBytes(totalSize))
                .Append('`')
                .Append(' ')
                .Append('`')
                .Append(percentUsed.ToString("P1", CultureInfo.CurrentCulture))
                .Append('`')
                .Append('\n');
        }

        return result.ToString();
    }

    /// <summary>
    ///     Escape Telegram MarkdownV2 special characters in normal text.
    /// </summary>
    private static string EscapeMarkdownV2(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Characters that must be escaped in MarkdownV2:
        // _ * [ ] ( ) ~ ` > # + - = | { } . !
        var specialChars = "_*[]()~`>#+-=|{}.!";

        var sb = new StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            if (specialChars.IndexOf(c) >= 0)
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
