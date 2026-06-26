using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Jellyfin.Plugin.TeleJelly.Services.Download.Health;
using Jellyfin.Plugin.TeleJelly.Services.Download.Torrents;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

[Category("Component")]
public class ServiceHealthMonitorTests
{
    [Test]
    public void GetAllServiceHealth_ReturnsOrderedSnapshot()
    {
        var monitor = new ServiceHealthMonitor([], [], new TelegramBotClientWrapper(), new NullLogger<ServiceHealthMonitor>());

        var values = monitor.GetAllServiceHealth().ToArray();

        Assert.That(values, Is.Not.Null);
        Assert.That(values, Is.Empty);
    }

    [Test]
    public async Task CheckAllServicesAsync_MarksServiceOfflineAfterRepeatedFailures()
    {
        var failingService = new FakeTorrentService("Transmission", testConnectionResult: false);
        var monitor = new ServiceHealthMonitor([failingService], [], new TelegramBotClientWrapper(), new NullLogger<ServiceHealthMonitor>());

        await monitor.CheckAllServicesAsync(CancellationToken.None);
        await monitor.CheckAllServicesAsync(CancellationToken.None);
        await monitor.CheckAllServicesAsync(CancellationToken.None);

        var health = monitor.GetServiceHealth("Transmission");

        Assert.That(health, Is.Not.Null);
        Assert.That(health!.State, Is.EqualTo(HealthState.Offline));
        Assert.That(health.ConsecutiveFailures, Is.EqualTo(3));
        Assert.That(monitor.GetAvailableTorrentServices(), Is.Empty);
    }

    [Test]
    public async Task CheckAllServicesAsync_KeepsHealthyServicesAvailable()
    {
        var healthyService = new FakeTorrentService("Transmission", testConnectionResult: true);
        var monitor = new ServiceHealthMonitor([healthyService], [], new TelegramBotClientWrapper(), new NullLogger<ServiceHealthMonitor>());

        await monitor.CheckAllServicesAsync(CancellationToken.None);

        var availableServices = monitor.GetAvailableTorrentServices().ToArray();
        var health = monitor.GetServiceHealth("Transmission");

        Assert.That(availableServices, Has.Length.EqualTo(1));
        Assert.That(availableServices[0].ServiceName, Is.EqualTo("Transmission"));
        Assert.That(health, Is.Not.Null);
        Assert.That(health!.State, Is.EqualTo(HealthState.Online));
    }

    [Test]
    public async Task CheckAllServicesAsync_ResetsFailureStateAfterRecovery()
    {
        var service = new FlappingTorrentService("Transmission", false, false, true);
        var monitor = new ServiceHealthMonitor([service], [], new TelegramBotClientWrapper(), new NullLogger<ServiceHealthMonitor>());

        await monitor.CheckAllServicesAsync(CancellationToken.None);
        await monitor.CheckAllServicesAsync(CancellationToken.None);
        await monitor.CheckAllServicesAsync(CancellationToken.None);

        var health = monitor.GetServiceHealth("Transmission");

        Assert.That(health, Is.Not.Null);
        Assert.That(health!.State, Is.EqualTo(HealthState.Online));
        Assert.That(health.ConsecutiveFailures, Is.Zero);
        Assert.That(monitor.GetAvailableTorrentServices().Select(x => x.ServiceName), Is.EqualTo(new[] { "Transmission" }));
    }

    [Test]
    public async Task CheckAllServicesAsync_RemovesDisabledServiceFromHealthState()
    {
        var service = new ToggleableTorrentService("Transmission", testConnectionResult: false);
        var monitor = new ServiceHealthMonitor([service], [], new TelegramBotClientWrapper(), new NullLogger<ServiceHealthMonitor>());

        await monitor.CheckAllServicesAsync(CancellationToken.None);
        service.Enabled = false;
        await monitor.CheckAllServicesAsync(CancellationToken.None);

        Assert.That(monitor.GetServiceHealth("Transmission"), Is.Null);
    }

    private sealed class FakeTorrentService : ITorrentDownloadService
    {
        private readonly bool _testConnectionResult;

        public FakeTorrentService(string serviceName, bool testConnectionResult)
        {
            ServiceName = serviceName;
            _testConnectionResult = testConnectionResult;
        }

        public string ServiceName { get; }

        public bool IsEnabled => true;

        public bool CanHandle(string linkOrMagnet) => true;

        public Task<string> AddDownloadAsync(string linkOrMagnet, CancellationToken ct) => Task.FromResult("download-id");

        public Task<object?> GetProgressAsync(string downloadId, CancellationToken ct) => Task.FromResult<object?>(null);

        public Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct) => Task.FromResult<FileInfo[]>([]);

        public Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> TestConnectionAsync(CancellationToken ct) => Task.FromResult(_testConnectionResult);
    }

    private sealed class FlappingTorrentService : ITorrentDownloadService
    {
        private readonly bool[] _results;
        private int _index;

        public FlappingTorrentService(string serviceName, params bool[] results)
        {
            ServiceName = serviceName;
            _results = results;
        }

        public string ServiceName { get; }

        public bool IsEnabled => true;

        public bool CanHandle(string linkOrMagnet) => true;

        public Task<string> AddDownloadAsync(string linkOrMagnet, CancellationToken ct) => Task.FromResult("download-id");

        public Task<object?> GetProgressAsync(string downloadId, CancellationToken ct) => Task.FromResult<object?>(null);

        public Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct) => Task.FromResult<FileInfo[]>([]);

        public Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> TestConnectionAsync(CancellationToken ct)
        {
            var result = _results[Math.Min(_index, _results.Length - 1)];
            _index++;
            return Task.FromResult(result);
        }
    }

    private sealed class ToggleableTorrentService : ITorrentDownloadService
    {
        private readonly bool _testConnectionResult;

        public ToggleableTorrentService(string serviceName, bool testConnectionResult)
        {
            ServiceName = serviceName;
            _testConnectionResult = testConnectionResult;
        }

        public string ServiceName { get; }

        public bool Enabled { get; set; } = true;

        public bool IsEnabled => Enabled;

        public bool CanHandle(string linkOrMagnet) => true;

        public Task<string> AddDownloadAsync(string linkOrMagnet, CancellationToken ct) => Task.FromResult("download-id");

        public Task<object?> GetProgressAsync(string downloadId, CancellationToken ct) => Task.FromResult<object?>(null);

        public Task<string?> GetDownloadDirectoryAsync(string downloadId, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task<FileInfo[]> GetCompletedFilesAsync(string downloadId, CancellationToken ct) => Task.FromResult<FileInfo[]>([]);

        public Task RemoveDownloadAsync(string downloadId, bool deleteFiles, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> TestConnectionAsync(CancellationToken ct) => Task.FromResult(_testConnectionResult);
    }
}
