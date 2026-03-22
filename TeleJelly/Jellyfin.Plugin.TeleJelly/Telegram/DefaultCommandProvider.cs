using System;
using System.Linq;
using Jellyfin.Plugin.TeleJelly.Telegram.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     Default implementation of ICommandProvider that scans the assembly for commands.
/// </summary>
internal class DefaultCommandProvider : ICommandProvider
{
    private readonly ICommandBase[] _commands;

    public DefaultCommandProvider(IServiceProvider serviceProvider)
    {
        _commands = GetType().Assembly.GetTypes()
            .Where(t =>
                typeof(ICommandBase).IsAssignableFrom(t) &&
                t is { IsClass: true, IsAbstract: false }
            )
            .Select(t => CreateCommand(t, serviceProvider))
            .ToArray();
    }

    public ICommandBase[] GetCommands()
    {
        return _commands;
    }

    private static ICommandBase CreateCommand(Type commandType, IServiceProvider serviceProvider)
    {
        try
        {
            // Try to create the command using DI
            return (ICommandBase)ActivatorUtilities.CreateInstance(serviceProvider, commandType);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to initialize Command: {commandType.FullName}. Error: {ex.Message}", ex);
        }
    }
}
