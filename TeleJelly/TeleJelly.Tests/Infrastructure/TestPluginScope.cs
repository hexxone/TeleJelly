using System;
using System.Reflection;
using System.Runtime.Serialization;
using Jellyfin.Plugin.TeleJelly;

namespace TeleJelly.Tests.Infrastructure;

internal sealed class TestPluginScope : IDisposable
{
    private readonly TeleJellyPlugin? _previousInstance;

    public TestPluginScope(PluginConfiguration configuration)
    {
        _previousInstance = TeleJellyPlugin.Instance;
#pragma warning disable SYSLIB0050
        var plugin = (TeleJellyPlugin)FormatterServices.GetUninitializedObject(typeof(TeleJellyPlugin));
#pragma warning restore SYSLIB0050
        SetConfiguration(plugin, configuration);
        SetInstance(plugin);
    }

    public void Dispose()
    {
        SetInstance(_previousInstance);
    }

    private static void SetInstance(TeleJellyPlugin? plugin)
    {
        var field = typeof(TeleJellyPlugin).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Unable to find TeleJellyPlugin.Instance backing field.");
        field.SetValue(null, plugin);
    }

    private static void SetConfiguration(TeleJellyPlugin plugin, PluginConfiguration configuration)
    {
        var property = typeof(TeleJellyPlugin).BaseType?.GetProperty("Configuration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.SetMethod != null)
        {
            property.SetValue(plugin, configuration);
            return;
        }

        var field = typeof(TeleJellyPlugin).BaseType?.GetField("<Configuration>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            throw new InvalidOperationException("Unable to set TeleJellyPlugin configuration for tests.");
        }

        field.SetValue(plugin, configuration);
    }
}
