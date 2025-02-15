using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Telegram.Commands;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     TODO
/// </summary>
public class TelegramBackgroundService : IHostedService, IDisposable
{
    private readonly TeleJellyPlugin _plugin;
    private readonly ILogger<TelegramBackgroundService> _logger;

    // keep the Commands here so they don't get initialized with the BotService everytime.
    private readonly CommandBase[] _commands;

    private string _currentToken = string.Empty;

    private TelegramBotService? _botService;

    /// <summary>
    ///     Creates a new instance of the TelegramBackgroundService
    /// </summary>
    /// <param name="plugin">Used for getting configuration and listening on changes to it.</param>
    /// <param name="logger">Used for printing service status and events.</param>
    /// <param name="serviceProvider">Used for instantiating the Commands with Dependency Injection.</param>
    public TelegramBackgroundService(TeleJellyPlugin plugin, ILogger<TelegramBackgroundService> logger, IServiceProvider serviceProvider)
    {
        _plugin = plugin;
        _logger = logger;

        _commands = plugin.GetType().Assembly.GetTypes()
            .Where(t =>
                t is { IsClass: true, IsAbstract: false } &&
                t.IsAssignableTo(typeof(CommandBase)) &&
                t.IsNotPublic)
            .Select(t => ActivatorUtilities.CreateInstance<CommandBase>(serviceProvider, t))
            .ToArray();

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
    ///
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe to configuration changes
        _plugin.ConfigurationChanged += _configHookOnOnConfigChange;

        // Initial configuration
        ConfigureBot(_plugin.Configuration);

        _logger.LogInformation("Telegram background service started");

        return Task.CompletedTask;
    }

    /// <summary>
    ///
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
            ConfigureBot(configuration);

            _logger.LogInformation("Telegram bot configuration changed");
        }
        else
        {
            _logger.LogError("BasePluginConfiguration is not of Type PluginConfiguration. Ignoring.");
        }
    }


    private void ConfigureBot(PluginConfiguration config)
    {
        var token = config.BotToken.Trim();

        // Don't reconfigure if token hasn't changed
        if (token == _currentToken)
        {
            return;
        }

        // Check if token is valid
        if (string.IsNullOrWhiteSpace(token) || token.Equals(Constants.DefaultBotToken))
        {
            _logger.LogInformation("Bot token is empty or default. Will not configure bot service.");
            DisposeBotService();
            return;
        }

        try
        {
            // Dispose old service if exists
            DisposeBotService();

            // Create and start new service
            _botService = new TelegramBotService(token, config, _logger, _commands);
            _botService.StartAsync().ConfigureAwait(false);
            _currentToken = token;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to configure bot service: {Msg}", ex.Message);
            DisposeBotService();
        }
    }

    private void DisposeBotService()
    {
        if (_botService != null)
        {
            _botService.Dispose();
            _botService = null;
            _logger.LogInformation("Telegram bot service disposed");
        }

        _currentToken = string.Empty;
    }

    /// <summary>
    ///
    /// </summary>
    public void Dispose()
    {
        DisposeBotService();
        GC.SuppressFinalize(this);
    }
}
