using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Cryptography;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NUglify;

#pragma warning disable CA2254

namespace Jellyfin.Plugin.TeleJelly.Api;

/// <summary>
///     Custom JellyFin implementation for the Telegram Login widget.
///     https://core.telegram.org/widgets/login
///     Will provide a dedicated Login-Flow for Telegram.
/// </summary>
[ApiController]
[Route("sso/[controller]")]
public class TelegramController : ControllerBase
{
    private readonly TeleJellyPlugin _instance;

    private readonly ILogger<TelegramController> _logger;
    private readonly TelegramHelper _telegramHelper;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TelegramController" /> class.
    /// </summary>
    /// <param name="logger">for outputting errors.</param>
    /// <param name="sessionManager">for manually logging in users.</param>
    /// <param name="userManager">for getting and creating users.</param>
    /// <param name="cryptoProvider">for hashing passwords.</param>
    /// <exception cref="Exception">if plugin was not properly initialized before usage.</exception>
    public TelegramController(ILogger<TelegramController> logger, ISessionManager sessionManager, IUserManager userManager, ICryptoProvider cryptoProvider)
    {
        if (TeleJellyPlugin.Instance == null)
        {
            throw new Exception("Plugin not initialized before Controller initialization.");
        }

        _instance = TeleJellyPlugin.Instance;
        _telegramHelper = new TelegramHelper(_instance, sessionManager, userManager, cryptoProvider, logger);
        _logger = logger;
        _logger.LogInformation("Telegram Controller initialized");
    }

    /// <summary>
    ///     Returns a simple Telegram-Login View with appropriate values for our Bot.
    ///     1. User will click on "Login with Telegram"
    ///     2. a Telegram.org popup opens, asking for login and bot permission
    ///     3. when confirmed by user -> will get redirected to "Confirm" method.
    /// </summary>
    /// <returns>Html Login Page.</returns>
    [HttpGet(nameof(Login))]
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
            .Replace("{{TELEGRAM_BOT_NAME}}", _instance.Configuration.BotUsername) // TODO get via BOT TOKEN
            .Replace("{{TELEGRAM_AUTH_URL}}", $"{serverUrl}/sso/Telegram/{nameof(Confirm)}");

        return Content(Minify(html, "text/html"), "text/html");
    }

    /// <summary>
    ///     Tries to log-in a Telegram-User from given Url Parameters.
    ///     1. Validates that a "hash" is present and it's correct
    ///     2. validates that user has "Username" and is whitelisted
    ///     3. get/creates the user and sets his permissions
    ///     4. we return a custom "login and redirect" Script
    ///     5. [Script] sets required values in localStorage for jellyfin
    ///     5. [Script] redirect to the dashboard.
    /// </summary>
    /// <param name="authData">Telegram Authenticated User data.</param>
    /// <returns>Custom Script.</returns>
    [HttpGet(nameof(Confirm))]
    public async Task<IActionResult> Confirm([FromQuery] SortedDictionary<string, string> authData)
    {
        var requestBase = _telegramHelper.GetRequestBase(Request);

        try
        {
            _telegramHelper.CheckTelegramAuthorizationImpl(authData);

            var user = await _telegramHelper.GetOrCreateJellyUser(authData);

            var authResult = await _telegramHelper.DoJellyUserAuth(Request, user);

            var view = _instance.TelegramRedirectPage;

            await using var stream = _instance.GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath);

            if (stream == null)
            {
                _logger.LogError("Failed to get resource {Resource}", view.EmbeddedResourcePath);
                return NotFound();
            }

            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();
            html = html
                .Replace("{{AUTH_RESULT_DATA}}", JsonSerializer.Serialize(authResult))
                .Replace("{{AUTH_REDIRECT_URL}}", $"{requestBase}");

            return Content(Minify(html, "text/html"), "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());

            var errorPageUrl = WebUtility.UrlEncode($"/{nameof(Login)}?error={ex.Message}");

            return await Task.FromResult<IActionResult>(Redirect(errorPageUrl));
        }
    }

    /// <summary>
    ///     Returns extra Files(treams).
    /// </summary>
    /// <param name="fileName">to search and return.</param>
    /// <returns>Stream of file.</returns>
    [HttpGet(nameof(ExtraFiles) + "/{fileName}")]
    public async Task<IActionResult> ExtraFiles([FromRoute] string fileName)
    {
        var view = _instance.GetExtraFiles().FirstOrDefault(extra => extra.Name == fileName);
        if (view == null)
        {
            return await Task.FromResult<IActionResult>(NotFound());
        }

        var stream = _instance.GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath);
        if (stream == null)
        {
            _logger.LogError("Failed to get resource {Resource}", view.EmbeddedResourcePath);
            return await Task.FromResult<IActionResult>(NotFound());
        }

        var mimeType = MimeTypes.GetMimeType(view.EmbeddedResourcePath);
        var memeTypes = new[] { "text/html", "text/css", "application/javascript", "application/x-javascript" };
        // dont try to minify binary files...like fonts... will end badly
        if (!memeTypes.Contains(mimeType))
        {
            return await Task.FromResult<IActionResult>(File(stream, mimeType));
        }

        using var reader = new StreamReader(stream);
        var html = await reader.ReadToEndAsync();
        return await Task.FromResult<IActionResult>(Content(Minify(html, mimeType), mimeType));
    }

    /// <summary>
    ///     Tries to minify the input and return it if the mimeType is known.
    ///     If there are minify errors or the Type is unknown, return input 1:1.
    ///     Supports: CSS, JS, HTMl.
    /// </summary>
    /// <param name="input">Source to process.</param>
    /// <param name="type">Mime-Type to check.</param>
    /// <returns>Minified input or input.</returns>
    public static string Minify(string input, string type)
    {
        switch (type)
        {
            case "text/html":
                var html = Uglify.Html(input);
                if (!html.HasErrors)
                {
                    return html.Code;
                }

                break;
            case "text/css":
                var css = Uglify.Js(input);
                if (!css.HasErrors)
                {
                    return css.Code;
                }

                break;
            case "application/javascript":
            case "application/x-javascript":
                var js = Uglify.Js(input);
                if (!js.HasErrors)
                {
                    return js.Code;
                }

                break;
            default:
                return input;
        }

        return input;
    }
}
