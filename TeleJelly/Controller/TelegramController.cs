using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes;
using Jellyfin.Plugin.TeleJelly.Telegram;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Branding;
using MediaBrowser.Model.Cryptography;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

#pragma warning disable CA2254

namespace Jellyfin.Plugin.TeleJelly.Controller;

/// <summary>
///     Custom JellyFin implementation for the Telegram Login widget.
///     https://core.telegram.org/widgets/login
///     Will provide a dedicated Login-Flow for Telegram.
/// </summary>
[ApiController]
[Route("sso/{Controller}")]
public class TelegramController : ControllerBase
{
    private readonly ILogger _logger;
    private readonly TeleJellyPlugin _instance;

    private readonly TelegramHelper _telegramHelper;

    private readonly BrandingOptions _brandingOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TelegramController" /> class.
    /// </summary>
    /// <param name="logger">for outputting errors.</param>
    /// <param name="sessionManager">for manually logging in users.</param>
    /// <param name="userManager">for getting and creating users.</param>
    /// <param name="cryptoProvider">for hashing passwords.</param>
    /// <param name="configurationManager">Instance of the <see cref="IConfigurationManager" /> interface.</param>
    /// <exception cref="Exception">if plugin was not properly initialized before usage.</exception>
    public TelegramController(
        ILogger<TelegramController> logger,
        ISessionManager sessionManager,
        IUserManager userManager,
        ICryptoProvider cryptoProvider,
        IConfigurationManager configurationManager)
    {
        _logger = logger;

        if (TeleJellyPlugin.Instance == null)
        {
            throw new Exception("Plugin not initialized before Controller initialization.");
        }

        _instance = TeleJellyPlugin.Instance;

        _telegramHelper = new TelegramHelper(_instance, sessionManager, userManager, cryptoProvider, logger);

        // stolen from https://github.com/jellyfin/jellyfin/blob/master/Jellyfin.Api/Controllers/BrandingController.cs
        _brandingOptions = configurationManager.GetConfiguration<BrandingOptions>("branding");

        _logger.LogDebug("Telegram Controller initialized");
    }

    /// <summary>
    ///     Returns a simple Telegram-Login View with appropriate values for our Bot.
    ///     1. User will click on "Login with Telegram"
    ///     2. a Telegram.org popup opens, asking for login and bot permission
    ///     3. when confirmed by user -> will get redirected to "Confirm" method.
    /// </summary>
    /// <returns>Html Login Page.</returns>
    [AllowAnonymous]
    [HttpGet(nameof(Login))]
    [Produces(MediaTypeNames.Text.Html)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login()
    {
        var view = _instance.TelegramLoginPage;

        await using var stream = _instance.GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath);

        if (stream == null)
        {
            _logger.LogError("Failed to get resource {Resource}", view.EmbeddedResourcePath);
            return NotFound();
        }

        var serverUrl = _telegramHelper.GetRequestBase(Request);

        using var reader = new StreamReader(stream);
        var html = await reader.ReadToEndAsync();
        html = html
            .Replace("{{SERVER_URL}}", serverUrl)
            .Replace("{{TELEGRAM_BOT_NAME}}", _instance.Configuration.BotUsername)
            .Replace("/*{{CUSTOM_CSS}}*/", _brandingOptions.CustomCss ?? string.Empty);

        return Content(html, "text/html");
    }

    /// <summary>
    ///     Tries to log-in a Telegram-User from given Url Parameters.
    ///     Returns a custom object, representing success or failure.
    ///     1. Validates that a "hash" is present and it's correct
    ///     2. validates that user has "Username" and is whitelisted
    ///     3. get/creates the user and sets his permissions
    ///     4. we return a custom "login object"
    ///     5. [Script] sets required values in localStorage for jellyfin
    ///     5. [Script] redirect to the dashboard.
    /// </summary>
    /// <param name="authData">Telegram Authenticated User data.</param>
    /// <returns>Custom Authentication Result.</returns>
    [AllowAnonymous]
    [HttpPost(nameof(Authenticate))]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SsoAuthenticationResult>> Authenticate([FromBody] SortedDictionary<string, string> authData)
    {
        var requestBase = _telegramHelper.GetRequestBase(Request);

        try
        {
            var telegramAuth = _telegramHelper.CheckTelegramAuthorizationImpl(authData);
            if (!telegramAuth.Ok)
            {
                return Unauthorized(new SsoAuthenticationResult { ServerAddress = requestBase, ErrorMessage = telegramAuth.ErrorMessage });
            }

            var user = await _telegramHelper.GetOrCreateJellyUser(authData);

            var authResult = await _telegramHelper.DoJellyUserAuth(Request, user);

            return Ok(new SsoAuthenticationResult { ServerAddress = requestBase, Ok = true, AuthenticatedUser = authResult });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());

            return StatusCode(500, new SsoAuthenticationResult { ServerAddress = requestBase, ErrorMessage = ex.ToString() });
        }
    }

    /// <summary>
    ///     Returns extra File-streams.
    /// </summary>
    /// <param name="fileName">to search and return.</param>
    /// <returns>Stream of file.</returns>
    [AllowAnonymous]
    [HttpGet(nameof(ExtraFiles) + "/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExtraFiles([FromRoute] string fileName)
    {
        var view = _instance.GetExtraFiles().FirstOrDefault(extra => extra.Name == fileName);
        if (view == null)
        {
            return NotFound();
        }

        var stream = _instance.GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath);
        if (stream == null)
        {
            _logger.LogError("Failed to get resource {Resource}", view.EmbeddedResourcePath);
            return StatusCode(500, $"Resource not found: {view.EmbeddedResourcePath}");
        }

        var mimeType = MimeTypes.GetMimeType(view.EmbeddedResourcePath);
        var memeTypes = new[] { "text/html", "text/css", "application/javascript", "application/x-javascript" };
        // dont try to minify binary files...like fonts... will end badly
        if (!memeTypes.Contains(mimeType))
        {
            return File(stream, mimeType);
        }

        var serverUrl = _telegramHelper.GetRequestBase(Request);

        // TODO find better way than this?
        using var reader = new StreamReader(stream);
        var html = await reader.ReadToEndAsync();
        var replaced = html.Replace("{{SERVER_URL}}", serverUrl);

        return Content(replaced, mimeType);
    }
}
