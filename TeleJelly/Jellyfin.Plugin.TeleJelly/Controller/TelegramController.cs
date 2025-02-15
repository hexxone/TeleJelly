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
    private static readonly string[] _entryPoints = ["index.html", "login", "login.html"];

    // private readonly ILogger _logger;
    private readonly TeleJellyPlugin _instance;

    private readonly TelegramLoginService _telegramLoginService;

    private readonly BrandingOptions _brandingOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TelegramController" /> class.
    /// </summary>
    /// <param name="instance">for getting the configuration</param>
    /// <param name="sessionManager">for manually logging in users.</param>
    /// <param name="userManager">for getting and creating users.</param>
    /// <param name="cryptoProvider">for hashing passwords.</param>
    /// <param name="configurationManager">Instance of the <see cref="IConfigurationManager" /> interface.</param>
    /// <exception cref="Exception">if plugin was not properly initialized before usage.</exception>
    public TelegramController(
        TeleJellyPlugin instance,
        ISessionManager sessionManager,
        IUserManager userManager,
        ICryptoProvider cryptoProvider,
        IConfigurationManager configurationManager)
    {
        _instance = instance;

        _telegramLoginService = new TelegramLoginService(instance, sessionManager, userManager, cryptoProvider);

        // stolen from https://github.com/jellyfin/jellyfin/blob/master/Jellyfin.Api/Controllers/BrandingController.cs
        _brandingOptions = configurationManager.GetConfiguration<BrandingOptions>("branding");

        // _logger.LogDebug("Telegram Controller initialized");
    }

    /// <summary>
    ///     Returns the HTML,CSS and JS File-streams of the login page.
    ///     Replaces certain string params and uses memory caching.
    ///
    ///     1. User will click on "Login with Telegram"
    ///     2. a Telegram.org popup opens, asking for login and bot permission
    ///     3. when confirmed by user -> will get redirected to "Confirm" method.
    ///     TODO: cache should be cleared when custom CSS changes.
    /// </summary>
    /// <param name="fileName">to search and return.</param>
    /// <returns>Stream of file.</returns>
    [AllowAnonymous]
    [HttpGet("{fileName=Login}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Files([FromRoute] string fileName)
    {
        var lowerFilename = fileName.ToLower();
        if (_entryPoints.Contains(lowerFilename))
        {
            lowerFilename = "index";
        }

        var view = Constants.LoginFiles.FirstOrDefault(extra => extra.Name == lowerFilename);
        if (view == null)
        {
            return NotFound($"Resource not found: '{lowerFilename}'");
        }

        var mimeType = MimeTypes.GetMimeType(view.EmbeddedResourcePath);
        if (!view.NeedsReplacement)
        {
            // don't try to replace strings in binary files like fonts...
            var binaryStream = GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath);
            if (binaryStream == null)
            {
                // _logger.LogError("Failed to get resource {Resource}", view.EmbeddedResourcePath);
                return StatusCode(500, $"Resource failed to load: {view.EmbeddedResourcePath}");
            }

            return File(binaryStream, mimeType);
        }

        var botUsername = _instance.Configuration.BotUsername;
        var serverUrl = Request.GetRequestBase(_instance.Configuration);

        // TODO fix cache DLL missing ? bruh
        // var cacheKey = $"{serverUrl}/sso/Telegram/{lowerFilename}/{botUsername}";
        // if (_instance.MemoryCache.Get<string>(cacheKey) is { } foundEntry)
        // {
        //     // serving from cache spares us opening the stream, reading it & replacing it.
        //     return Content(foundEntry, mimeType);
        // }

        var textStream = GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath);
        if (textStream == null)
        {
            // _logger.LogError("Failed to get resource {Resource}", view.EmbeddedResourcePath);
            return StatusCode(500, $"Resource failed to load: {view.EmbeddedResourcePath}");
        }

        using var reader = new StreamReader(textStream);
        var html = await reader.ReadToEndAsync();
        var replaced = html
            .Replace("{{SERVER_URL}}", serverUrl)
            .Replace("{{TELEGRAM_BOT_NAME}}", botUsername)
            .Replace("/*{{CUSTOM_CSS}}*/", _brandingOptions.CustomCss ?? string.Empty);

        // var cacheEntryOptions = new MemoryCacheEntryOptions()
        //     .SetSlidingExpiration(TimeSpan.FromMinutes(5))
        //     .SetAbsoluteExpiration(TimeSpan.FromMinutes(60))
        //     .SetPriority(CacheItemPriority.Low);
        //
        // _instance.MemoryCache.Set(cacheKey, replaced, cacheEntryOptions);

        return Content(replaced, mimeType);
    }

    /// <summary>
    ///     Tries to log in a Telegram-User from given Url Parameters.
    ///     Returns a custom object, representing success or failure.
    ///     1. Validates that a "hash" is present, and it's correct
    ///     2. validates that user has "Username" and is whitelisted
    ///     3. get/creates the user and sets his permissions
    ///     4. we return a custom "login object"
    ///     5. [Script] sets required values in localStorage for Jellyfin
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
        var requestBase = Request.GetRequestBase(_instance.Configuration);

        try
        {
            var telegramAuth = _telegramLoginService.CheckTelegramAuthorizationImpl(authData);
            if (!telegramAuth.Ok)
            {
                return Unauthorized(new SsoAuthenticationResult { ServerAddress = requestBase, ErrorMessage = telegramAuth.ErrorMessage });
            }

            var user = await _telegramLoginService.GetOrCreateJellyUser(authData);

            var authResult = await _telegramLoginService.DoJellyUserAuth(Request, user);

            return Ok(new SsoAuthenticationResult { ServerAddress = requestBase, Ok = true, AuthenticatedUser = authResult });
        }
        catch (Exception ex)
        {
            // _logger.LogError(ex.ToString());

            return StatusCode(500, new SsoAuthenticationResult { ServerAddress = requestBase, ErrorMessage = ex.Message });
        }
    }
}
