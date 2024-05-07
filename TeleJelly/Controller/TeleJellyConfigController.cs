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
    /// <param name="configurationManager">Manages the Jellyfin config.</param>
    public TeleJellyConfigController(IConfigurationManager configurationManager)
    {
        if (TeleJellyPlugin.Instance == null)
        {
            throw new Exception("Plugin not initialized before Controller initialization.");
        }

        _pluginConfiguration = TeleJellyPlugin.Instance.Configuration;

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

            var botInfo = await botClient.GetMeAsync(ct.Token);

            return Ok(new ValidateBotTokenResponse { Ok = true, BotUsername = botInfo.Username! });
        }
        catch (Exception)
        {
            return StatusCode(500, new ValidateBotTokenResponse { ErrorMessage = "Invalid Token" });
        }
    }

    /// <summary>
    ///     Sets a new Login Page Disclaimer message with a link to the SSO page.
    /// </summary>
    /// <returns>new Disclaimer string.</returns>
    [HttpPost(nameof(SetLoginDisclaimer))]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<bool> SetLoginDisclaimer()
    {
        var serverUrl = Request.GetRequestBase(_pluginConfiguration);

        var disclaimer = $@"
<form action=""{serverUrl}/sso/Telegram"">
    <button is=""emby-button"" style=""display: flex;"" class=""block emby-button raised button-submit"">
        Sign in with Telegram
        <svg viewBox=""0 0 240 240"" xmlns=""http://www.w3.org/2000/svg"" style=""max-height:4.20em;"">
            <defs>
                <linearGradient gradientUnits=""userSpaceOnUse"" x2=""120"" y1=""240"" x1=""120"" id=""linear-gradient"">
                    <stop stop-color=""#1d93d2"" offset=""0""></stop>
                    <stop stop-color=""#38b0e3"" offset=""1""></stop>
                </linearGradient>
            </defs>
            <title>Telegram_logo</title>
            <circle fill=""url(#linear-gradient)"" r=""120"" cy=""120"" cx=""120""></circle>
            <path fill=""#fff"" d=""M81.486,130.178,52.2,120.636s-3.5-1.42-2.373-4.64c.232-.664.7-1.229,2.1-2.2,6.489-4.523,120.106-45.36,120.106-45.36s3.208-1.081,5.1-.362a2.766,2.766,0,0,1,1.885,2.055,9.357,9.357,0,0,1,.254,2.585c-.009.752-.1,1.449-.169,2.542-.692,11.165-21.4,94.493-21.4,94.493s-1.239,4.876-5.678,5.043A8.13,8.13,0,0,1,146.1,172.5c-8.711-7.493-38.819-27.727-45.472-32.177a1.27,1.27,0,0,1-.546-.9c-.093-.469.417-1.05.417-1.05s52.426-46.6,53.821-51.492c.108-.379-.3-.566-.848-.4-3.482,1.281-63.844,39.4-70.506,43.607A3.21,3.21,0,0,1,81.486,130.178Z""></path>
        </svg>
    </button>
</form>";

        _brandingOptions.LoginDisclaimer = disclaimer;

        return Ok(true);
    }
}
