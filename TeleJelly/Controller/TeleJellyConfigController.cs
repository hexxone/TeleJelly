using System;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;

namespace Jellyfin.Plugin.TeleJelly.Controller;

/// <summary>
///     Todo.
/// </summary>
[ApiController]
[Route("api/{Controller}")]
[Authorize(Policy = "RequiresElevation")]
public class TeleJellyConfigController : ControllerBase
{
    /// <summary>
    ///     Todo.
    /// </summary>
    /// <param name="request">Wip.</param>
    /// <returns>Amk.</returns>
    [HttpPost(nameof(ValidateBotToken))]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ValidateBotTokenResponse>> ValidateBotToken([FromBody] ValidateBotTokenRequest request)
    {
        return Ok(await ValidateToken(request.Token));
    }

    private static async Task<ValidateBotTokenResponse> ValidateToken(string token)
    {
        try
        {
            var botClient = new TelegramBotClient(token);

            var tokenValid = await botClient.TestApiAsync();
            if (!tokenValid)
            {
                return new ValidateBotTokenResponse { ErrorMessage = "Invalid Token." };
            }

            var botInfo = await botClient.GetMeAsync();

            return new ValidateBotTokenResponse { Ok = true, BotUsername = botInfo.Username! };
        }
        catch (Exception ex)
        {
            return new ValidateBotTokenResponse { ErrorMessage = $"Error: '{ex.GetType().Name}'" };
        }
    }
}
