using System;
using System.Diagnostics;
using System.Threading;

namespace Jellyfin.Plugin.TeleJelly.Services.Download.Search;

public sealed class SearchProgress
{
    private const double EstimatedSecondsPerWorkUnit = 5.0;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _completedProviders;
    private int _completedWorkUnits;
    private int _totalProviders;
    private int _totalWorkUnits;
    private string _phase = "Starting";

    internal void Configure(int providerCount, int queryCount)
    {
        Volatile.Write(ref _totalProviders, providerCount);
        Volatile.Write(ref _totalWorkUnits, providerCount * queryCount);
        Volatile.Write(ref _phase, "Searching providers");
    }

    internal void AddLinkValidationWork(int count)
    {
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref _totalWorkUnits, count);
        Volatile.Write(ref _phase, "Checking download links");
    }

    internal void CompleteWorkUnit()
    {
        Interlocked.Increment(ref _completedWorkUnits);
    }

    internal void CompleteProvider()
    {
        Interlocked.Increment(ref _completedProviders);
    }

    internal SearchProgressSnapshot GetSnapshot()
    {
        var totalProviders = Volatile.Read(ref _totalProviders);
        var completedProviders = Volatile.Read(ref _completedProviders);
        var totalWorkUnits = Volatile.Read(ref _totalWorkUnits);
        var completedWorkUnits = Math.Min(Volatile.Read(ref _completedWorkUnits), totalWorkUnits);
        var elapsed = _stopwatch.Elapsed;
        var remainingUnits = Math.Max(0, totalWorkUnits - completedWorkUnits);
        var concurrency = Math.Max(1, Math.Min(HttpClientSearchConcurrency, totalProviders));
        var secondsPerUnit = completedWorkUnits > 0
            ? Math.Max(0.25, elapsed.TotalSeconds * concurrency / completedWorkUnits)
            : EstimatedSecondsPerWorkUnit;
        var estimatedRemaining = TimeSpan.FromSeconds(remainingUnits * secondsPerUnit / concurrency * 1.25);
        var percent = totalWorkUnits == 0
            ? 0
            : (int)Math.Clamp(Math.Round(100d * completedWorkUnits / totalWorkUnits), 0, 99);

        return new SearchProgressSnapshot(
            Volatile.Read(ref _phase),
            completedProviders,
            totalProviders,
            completedWorkUnits,
            totalWorkUnits,
            percent,
            estimatedRemaining);
    }

    private const int HttpClientSearchConcurrency = 4;
}

internal sealed record SearchProgressSnapshot(
    string Phase,
    int CompletedProviders,
    int TotalProviders,
    int CompletedWorkUnits,
    int TotalWorkUnits,
    int Percent,
    TimeSpan EstimatedRemaining);
