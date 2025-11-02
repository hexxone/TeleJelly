using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Jellyfin.Plugin.TeleJelly.Telegram.Commands;

/// <summary>
///     Command for printing the server infos.
/// </summary>
// ReSharper disable once UnusedType.Global
public class CommandStats : ICommandBase
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

        // add Jellyfin Public-Url to Msg if set
        var baseUrl = telegramBotService._config.LoginBaseUrl;
        var serverUrl = baseUrl != null ? $"Server URL: {baseUrl}\n" : "";

        return $"📊 TeleJelly Stats 📊\n\n" +
               $"🖥️ Jellyfin Server\n" +
               serverUrl +
               $"Server Name: {serverApplicationHost.Name}\n" +
               $"Version: {serverApplicationHost.ApplicationVersion}\n" +
               $"Uptime: {FormatTimeSpan(serverUptime)}\n" +
               (isAdmin ? $"Process Memory: {FormatBytes(workingSet)}\n" : "") +
               (isAdmin ? $"System Memory: {FormatBytes(totalPhysicalMemory)}\n\n" : "") +
               $"🤖 Telegram Bot\n" +
               $"Uptime: {botUptimeText}\n\n" +
               (isAdmin ? $"💾 Disk Space\n" : "") +
               (isAdmin ? GetDiskInfo() : "");
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
