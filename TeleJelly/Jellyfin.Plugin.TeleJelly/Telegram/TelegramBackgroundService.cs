using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///
/// </summary>
public class TelegramBackgroundService : IHostedService, IDisposable
{
    private readonly TeleJellyPlugin _plugin;
    private readonly ILogger<TelegramBackgroundService> _logger;

    private string _currentToken = string.Empty;

    private TelegramBotService? _botService;

    /// <summary>
    ///
    /// </summary>
    /// <param name="plugin"></param>
    /// <param name="logger"></param>
    public TelegramBackgroundService(TeleJellyPlugin plugin, ILogger<TelegramBackgroundService> logger)
    {
        _plugin = plugin;
        _logger = logger;
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
            _botService = new TelegramBotService(token, config, _logger);
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
