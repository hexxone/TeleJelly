using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes;
using Jellyfin.Plugin.TeleJelly.Services;
using MediaBrowser.Controller.Providers;
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
    private readonly IProviderManager _providerManager;
    private readonly RequestService _requestService;

    /// <summary>
    ///     Helper Controller for the TeleJelly configuration page.
    ///     Provides methods to validate Telegram Bot Tokens and manage stored media requests.
    /// </summary>
    public TeleJellyConfigController(RequestService requestService, IProviderManager providerManager)
    {
        _requestService = requestService ?? throw new ArgumentNullException(nameof(requestService));
        _providerManager = providerManager ?? throw new ArgumentNullException(nameof(providerManager));
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

    /// <summary>
    ///     Adds a media request by IMDb ID, resolving metadata through Jellyfin providers.
    /// </summary>
    [HttpPost(nameof(AddRequest))]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MediaRequest>> AddRequest([FromBody] AddRequestRequest? request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ImdbId))
        {
            return BadRequest();
        }

        var imdbId = request.ImdbId.Trim();

        var (title, year, found) = await MetadataResolver
            .FindRemoteMetadataAsync(_providerManager, imdbId, cancellationToken)
            .ConfigureAwait(false);

        if (!found)
        {
            return NotFound();
        }

        var mediaRequest = new MediaRequest
        {
            ItemId = Guid.Empty,
            ImdbId = imdbId,
            Title = title,
            Year = year,
            UserId = "Manual",
            UserDisplayName = "Admin",
            RequestedAtUtc = DateTime.UtcNow
        };

        var result = await _requestService
            .TryAddRequestAsync(mediaRequest, 0, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            RequestAddResult.Duplicate => Conflict(),
            RequestAddResult.Added => Ok(mediaRequest),
            RequestAddResult.Removed => Ok(mediaRequest), // unlikely for manual user
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    /// <summary>
    ///     Removes a request by IMDb ID.
    /// </summary>
    [HttpDelete(nameof(RemoveRequest) + "/{imdbId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveRequest(string imdbId, CancellationToken cancellationToken)
    {
        await _requestService.RemoveRequestAsync(imdbId, cancellationToken).ConfigureAwait(false);
        return Ok();
    }

    /// <summary>
    ///     Unlinks a Telegram chat from a TeleJelly group.
    /// </summary>
    /// <param name="groupName">The name of the group to unlink.</param>
    /// <returns>Success status.</returns>
    [HttpPost(nameof(UnlinkGroup) + "/{groupName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult UnlinkGroup(string groupName)
    {
        var config = TeleJellyPlugin.Instance?.Configuration;
        if (config == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "Plugin configuration not available");
        }

        var group = config.TelegramGroups?.Find(g => g.GroupName == groupName);
        if (group == null)
        {
            return NotFound($"Group '{groupName}' not found");
        }

        // Unlink by clearing the TelegramGroupChat
        group.TelegramGroupChat = null;

        // Save the configuration
        TeleJellyPlugin.Instance!.SaveConfiguration(config);

        return Ok();
    }
}

/// <summary>
///     DTO for adding a manual request from the configuration page.
/// </summary>
public class AddRequestRequest
{
    public string ImdbId { get; set; } = string.Empty;
}
