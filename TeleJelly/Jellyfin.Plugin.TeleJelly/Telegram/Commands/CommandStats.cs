using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

internal class CommandStats(
    TelegramBotService telegramBotService,
    IServerApplicationHost serverApplicationHost) : CommandBase(telegramBotService)
{
    internal override string Command => "stats";
    internal override bool NeedsAdmin => true;

    internal override async Task Execute(ITelegramBotClient botClient, Message message, bool isAdmin, CancellationToken cancellationToken)
    {
        // Generate stats message
        var statsMessage = GetSystemStats();

        await botClient.SendMessage(
            message.Chat.Id,
            statsMessage,
            cancellationToken: cancellationToken);
    }

    private string GetSystemStats()
    {
        var process = Process.GetCurrentProcess();

        // Calculate bot uptime - handle nullable TimeSpan
        string botUptimeText;
        if (_telegramBotService._startTime.HasValue)
        {
            var botUptime = DateTime.Now - _telegramBotService._startTime.Value;
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

        // Format message
        return $"📊 TeleJelly Stats 📊\n\n" +
               $"🖥️ Jellyfin Server\n" +
               $"Server Name: {serverApplicationHost.Name}\n" +
               $"Version: {serverApplicationHost.ApplicationVersion}\n" +
               $"Uptime: {FormatTimeSpan(serverUptime)}\n" +
               $"Process Memory: {FormatBytes(workingSet)}\n" +
               $"System Memory: {FormatBytes(totalPhysicalMemory)}\n\n" +
               $"🤖 Telegram Bot\n" +
               $"Uptime: {botUptimeText}\n\n" +
               $"💾 Disk Space\n" +
               GetDiskInfo();
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
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
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
        var result = "";

        // Get all drives
        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();

        foreach (var drive in drives)
        {
            var totalSize = drive.TotalSize;
            var freeSpace = drive.AvailableFreeSpace;
            var usedSpace = totalSize - freeSpace;
            var percentUsed = (double)usedSpace / totalSize;

            result += $"{drive.Name} {FormatBytes(usedSpace)}/{FormatBytes(totalSize)} ({percentUsed:P1})\n";
        }

        return result;
    }
}
