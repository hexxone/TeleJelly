using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Services;
using Jellyfin.Plugin.TeleJelly.Telegram.Commands;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     The TeleJelly Background service which (re-)initializes Telegram the bot-service when the botToken changes.
/// </summary>
public class TelegramBackgroundService : IHostedService, IDisposable
{
    private readonly TeleJellyPlugin _plugin;
    private readonly ILogger<TelegramBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TelegramBotClientWrapper _botClientWrapper;

    // keep the Commands here so they don't get initialized with the BotService everytime.
    private readonly ICommandBase[] _commands;

    private string _currentToken = string.Empty;

    private TelegramBotService? _botService;

    /// <summary>
    ///     Creates a new instance of the TelegramBackgroundService
    /// </summary>
    /// <param name="logger">Used for printing service status and events.</param>
    /// <param name="serviceProvider">Used for instantiating the Commands with Dependency Injection.</param>
    /// <param name="botClientWrapper"></param>
    public TelegramBackgroundService(ILogger<TelegramBackgroundService> logger, IServiceProvider serviceProvider, TelegramBotClientWrapper botClientWrapper)
    {
        _plugin = TeleJellyPlugin.Instance ?? throw new ArgumentException("TeleJellyPlugin Instance null.");
        _logger = logger;
        _serviceProvider = serviceProvider;
        _botClientWrapper = botClientWrapper;

        _commands = _plugin.GetType().Assembly.GetTypes()
            .Where(t =>
                typeof(ICommandBase).IsAssignableFrom(t) &&
                t is { IsClass: true, IsAbstract: false }
            )
            .Select(t => Activator.CreateInstance(t) as ICommandBase
                         ?? throw new Exception($"Failed to initialize Command: {t.FullName}"))
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
        if (!config.EnableBotService ||string.IsNullOrWhiteSpace(newToken) || newToken.Equals(Constants.DefaultBotToken))
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
            // Create and start new service
            _botService = new TelegramBotService(_serviceProvider, _logger, _commands, newToken, config);
            _botClientWrapper.Client = _botService._client;
            _botService.StartAsync().ConfigureAwait(false);
            _currentToken = newToken;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to configure Telegram bot service: {Msg}", ex.Message);
            DisposeBotService();
        }
    }

    /// <summary>
    ///     Game-End the bot.
    /// </summary>
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
    ///     Game-End the background service.
    /// </summary>
    public void Dispose()
    {
        DisposeBotService();
        GC.SuppressFinalize(this);
    }
}
