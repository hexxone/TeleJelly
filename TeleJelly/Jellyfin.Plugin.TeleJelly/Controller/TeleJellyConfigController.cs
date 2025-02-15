using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Branding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;

namespace Jellyfin.Plugin.TeleJelly.Controller;

/// <summary>
///     Helper Controller for the TeleJelly configuration page.
///     Can validate Telegram Bot Tokens and update the LoginDisclaimer configuration.
/// </summary>
[ApiController]
[Route("api/{Controller}")]
[Authorize(Policy = "RequiresElevation")]
public class TeleJellyConfigController : ControllerBase
{
    private readonly PluginConfiguration _pluginConfiguration;
    private readonly BrandingOptions _brandingOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TeleJellyConfigController"/> class.
    /// </summary>
    public TeleJellyConfigController(TeleJellyPlugin plugin, IConfigurationManager configurationManager)
    {
        _pluginConfiguration = plugin.Configuration;

        _brandingOptions = configurationManager.GetConfiguration<BrandingOptions>("branding");
    }

    /// <summary>
    ///     Validates a Telegram Bot Token against the official API.
    /// </summary>
    /// <param name="request">Bot token to validate.</param>
    /// <returns>Amk.</returns>
    [HttpPost(nameof(ValidateBotToken))]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ValidateBotTokenResponse>> ValidateBotToken([FromBody] ValidateBotTokenRequest request)
    {
        try
        {
            var botClient = new TelegramBotClient(request.Token);

            // sometimes the api is reeeeaaally slow...
            using var ct = new CancellationTokenSource(TimeSpan.FromMilliseconds(10000));

            var botInfo = await botClient.GetMe(ct.Token);

            return Ok(new ValidateBotTokenResponse { Ok = true, BotUsername = botInfo.Username! });
        }
        catch (Exception)
        {
            return StatusCode(500, new ValidateBotTokenResponse { ErrorMessage = "Invalid Token" });
        }
    }
}
