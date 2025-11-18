using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes;
using Jellyfin.Plugin.TeleJelly.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;

namespace Jellyfin.Plugin.TeleJelly.Controller;

/// <summary>
///     Helper Controller for the TeleJelly configuration page.
///     Provides methods to validate Telegram Bot Tokens and manage stored media requests.
///     - "RequiresElevation" means only Admins should be able to access this.
/// </summary>
[ApiController]
[Route("api/{Controller}")]
[Authorize(Policy = "RequiresElevation")]
public class TeleJellyConfigController : ControllerBase
{
    private readonly RequestService _requestService;

    /// <summary>
    ///     Helper Controller for the TeleJelly configuration page.
    ///     Provides methods to validate Telegram Bot Tokens and manage stored media requests.
    /// </summary>
    public TeleJellyConfigController(RequestService requestService)
    {
        _requestService = requestService ?? throw new ArgumentNullException(nameof(requestService));
    }

    /// <summary>
    ///     Validates a Telegram Bot Token against the official API.
    /// </summary>
    /// <param name="request">Bot token to validate.</param>
    /// <returns>Validation result.</returns>
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

            // sometimes the api is reeeeaaally slow... or just throttling requests ?
            using var ct = new CancellationTokenSource(TimeSpan.FromMilliseconds(10000));

            var botInfo = await botClient.GetMe(ct.Token);

            return Ok(new ValidateBotTokenResponse { Ok = true, BotUsername = botInfo.Username! });
        }
        catch (Exception)
        {
            return StatusCode(500, new ValidateBotTokenResponse { ErrorMessage = "Invalid Token" });
        }
    }

    /// <summary>
    ///     Gets the current list of media requests.
    /// </summary>
    [HttpGet(nameof(GetRequests))]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<MediaRequest>>> GetRequests(CancellationToken cancellationToken)
    {
        var requests = await _requestService.GetRequestsAsync(cancellationToken).ConfigureAwait(false);
        return Ok(requests);
    }

    /// <summary>
    ///     Replaces the current list of media requests.
    /// </summary>
    [HttpPost(nameof(SetRequests))]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetRequests([FromBody] List<MediaRequest> requests, CancellationToken cancellationToken)
    {
        await _requestService.SetRequestsAsync(requests, cancellationToken).ConfigureAwait(false);
        return Ok();
    }
}
