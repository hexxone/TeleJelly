using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Telegram.Commands;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     Interface for providing Telegram commands.
/// </summary>
public interface ICommandProvider
{
    /// <summary>
    ///     Gets the registered commands.
    /// </summary>
    ICommandBase[] GetCommands();
}

/// <summary>
///     Default implementation of ICommandProvider that scans the assembly for commands.
/// </summary>
public class DefaultCommandProvider : ICommandProvider
{
    private readonly ICommandBase[] _commands;

    public DefaultCommandProvider()
    {
        _commands = GetType().Assembly.GetTypes()
            .Where(t =>
                typeof(ICommandBase).IsAssignableFrom(t) &&
                t is { IsClass: true, IsAbstract: false }
            )
            .Select(t => Activator.CreateInstance(t) as ICommandBase
                         ?? throw new Exception($"Failed to initialize Command: {t.FullName}"))
            .ToArray();
    }

    public ICommandBase[] GetCommands()
    {
        return _commands;
    }
}

/// <summary>
///     The TeleJelly Background service which (re-)initializes Telegram the bot-service when the botToken changes.
/// </summary>
public sealed class TelegramBackgroundService : IHostedService, IDisposable
{
    private const int InactivityCheckIntervalMinutes = 30; // Check every 30 minutes
    private const int InactivityThresholdHours = 24; // Reconfigure after 24 hours of inactivity
    private readonly TelegramBotClientWrapper _botClientWrapper;
    private readonly ICommandBase[] _commands;
    private readonly ILogger<TelegramBackgroundService> _logger;
    private readonly TeleJellyPlugin _plugin;

    private readonly IServiceProvider _serviceProvider;

    private TelegramBotService? _botService;

    private string _currentToken = string.Empty;
    private Timer? _inactivityTimer;

    /// <summary>
    ///     Creates a new instance of the TelegramBackgroundService
    /// </summary>
    /// <param name="serviceProvider">Used for giving bot commands the possibility to resolve dependencies independently</param>
    /// <param name="logger">Used for printing service status and events.</param>
    /// <param name="commandProvider">Used for providing the available commands for the bot to execute</param>
    /// <param name="botClientWrapper">Used for holding a global reference to the Telegram Bot Client</param>
    public TelegramBackgroundService(IServiceProvider serviceProvider, ILogger<TelegramBackgroundService> logger,
        TelegramBotClientWrapper botClientWrapper, ICommandProvider commandProvider)
    {
        _plugin = TeleJellyPlugin.Instance ?? throw new ArgumentException("TeleJellyPlugin Instance null.");
        _logger = logger;
        _botClientWrapper = botClientWrapper;
        _serviceProvider = serviceProvider;

        _commands = commandProvider.GetCommands();
        var commandNames = _commands.Select(c => c.Command).ToArray();

        // Find any duplicate command names
        var duplicateCommands = commandNames
            .GroupBy(x => x.ToLower())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateCommands.Any())
        {
            throw new InvalidOperationException(
                $"Duplicate command names found: {string.Join(", ", duplicateCommands)}. " +
                "Each command must have a unique name.");
        }

        _logger.LogInformation("Registered '{Count}' Telegram Bot Commands: [{CommandNames}]", _commands.Length, string.Join(", ", commandNames));
    }

    /// <summary>
    ///     Game-End the background service.
    /// </summary>
    public void Dispose()
    {
        DisposeBotService();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     ASP Start-hook for the Background Service
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe to configuration changes
        _plugin.ConfigurationChanged += _configHookOnOnConfigChange;

        // Initial configuration
        ConfigureBot(_plugin.Configuration);

        // Start inactivity check timer
        _inactivityTimer = new Timer(
            CheckForInactivity,
            null,
            TimeSpan.FromMinutes(InactivityCheckIntervalMinutes),
            TimeSpan.FromMinutes(InactivityCheckIntervalMinutes));

        _logger.LogInformation("Telegram background service started");

        return Task.CompletedTask;
    }

    /// <summary>
    ///     ASP Stop-hook for the Background Service
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _plugin.ConfigurationChanged -= _configHookOnOnConfigChange;

        DisposeBotService();

        _logger.LogInformation("Telegram background service stopped");

        return Task.CompletedTask;
    }

    private void _configHookOnOnConfigChange(object? sender, BasePluginConfiguration baseConfig)
    {
        if (baseConfig is PluginConfiguration configuration)
        {
            _logger.LogInformation("Telegram bot configuration changed. Configuring...");

            ConfigureBot(configuration);
        }
        else
        {
            _logger.LogError("BasePluginConfiguration is not of Type PluginConfiguration. Ignoring: {TypeName}", baseConfig.GetType().FullName);
        }
    }


    private void ConfigureBot(PluginConfiguration config)
    {
        var newToken = config.BotToken.Trim();
        if (!config.EnableBotService || string.IsNullOrWhiteSpace(newToken) || newToken.Equals(Constants.DefaultBotToken))
        {
            DisposeBotService();
            _logger.LogInformation("Telegram bot service deactivated, token empty or invalid.");
            return;
        }

        if (newToken == _currentToken)
        {
            _logger.LogInformation("Telegram bot token is unchanged. Will not re-configure service.");
            _botService?.UpdateConfig(config);
            return;
        }

        // Dispose old service if exists
        DisposeBotService();

        try
        {
            // Create and start a new service
            var logger = _serviceProvider.GetRequiredService<ILogger<TelegramBotService>>();
            var libraryManager = _serviceProvider.GetRequiredService<ILibraryManager>();
            _botService = new TelegramBotService(logger, newToken, config, _serviceProvider, _botClientWrapper, _commands, libraryManager);
            _botService.StartAsync().ConfigureAwait(false);
            _currentToken = newToken;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to configure Telegram bot service: {Msg}", ex.Message);
            DisposeBotService();
        }
    }

    private void CheckForInactivity(object? state)
    {
        if (_botService?.StartTime == null)
        {
            return; // Bot not running
        }

        var inactivityDuration = DateTime.UtcNow - _botService.LastActivityTime;
        if (inactivityDuration.TotalHours < InactivityThresholdHours)
        {
            return; // still active in Timeframe
        }

        _logger.LogInformation(
            "Telegram bot has been inactive for {Hours} hours. Triggering automatic reconfiguration...",
            inactivityDuration.TotalHours);

        // Trigger reconfiguration
        ConfigureBot(_plugin.Configuration);
    }

    /// <summary>
    ///     Game-End the bot.
    /// </summary>
    private void DisposeBotService()
    {
        _inactivityTimer?.Dispose();
        _inactivityTimer = null;

        if (_botService != null)
        {
            _botService.Dispose();
            _botService = null;
            _logger.LogInformation("Telegram bot service disposed");
        }

        _currentToken = string.Empty;
    }
}
