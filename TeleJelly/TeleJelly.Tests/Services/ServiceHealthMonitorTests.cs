using System.Linq;
using Jellyfin.Plugin.TeleJelly.Services.Download;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace TeleJelly.Tests.Services;

public class ServiceHealthMonitorTests
{
    [Test]
    public void GetAllServiceHealth_ReturnsOrderedSnapshot()
    {
        var monitor = new ServiceHealthMonitor([], [], new NullLogger<ServiceHealthMonitor>());

        var values = monitor.GetAllServiceHealth().ToArray();

        Assert.That(values, Is.Not.Null);
        Assert.That(values, Is.Empty);
    }
}
