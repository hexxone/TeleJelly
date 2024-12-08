using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///
/// </summary>
public class TelegramBackgroundService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TelegramBackgroundService> _logger;

    private IDisposable? _configChangeRegistration;
    private string _currentToken = string.Empty;

    private TelegramBotService? _botService;

    /// <summary>
    ///
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <param name="logger"></param>
    public TelegramBackgroundService(IServiceProvider serviceProvider, ILogger<TelegramBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Get plugin instance from DI
        var plugin = _serviceProvider.GetRequiredService<TeleJellyPlugin>();

        // Subscribe to configuration changes
        _configChangeRegistration = plugin.ConfigurationChangeToken.RegisterChangeCallback(
            _ => OnConfigurationChanged(), null);

        // Initial configuration
        ConfigureBot(plugin.Configuration);

        _logger.LogInformation("Telegram bot service started");

        return Task.CompletedTask;
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _configChangeRegistration?.Dispose();
        DisposeBotService();

        _logger.LogInformation("Telegram bot service stopped");

        return Task.CompletedTask;
    }

    private void OnConfigurationChanged()
    {
        var plugin = _serviceProvider.GetRequiredService<TeleJellyPlugin>();
        ConfigureBot(plugin.Configuration);

        // Re-register for next change
        _configChangeRegistration?.Dispose();
        _configChangeRegistration = plugin.ConfigurationChangeToken.RegisterChangeCallback(
            _ => OnConfigurationChanged(), null);

        _logger.LogInformation("Telegram bot configuration changed");
    }

    private void ConfigureBot(PluginConfiguration config)
    {
        var token = config.BotToken.Trim();

        // Don't reconfigure if token hasn't changed
        if (token == _currentToken)
            return;

        // Check if token is valid
        if (string.IsNullOrWhiteSpace(token) || token.Equals(Constants.DefaultBotToken))
        {
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
        }
        _currentToken = string.Empty;

        _logger.LogInformation("Telegram bot service disposed");
    }

    /// <summary>
    ///
    /// </summary>
    public void Dispose()
    {
        _configChangeRegistration?.Dispose();
        DisposeBotService();
        GC.SuppressFinalize(this);
    }
}
