using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TeleJelly.Classes;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

#pragma warning disable CA2254

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     Telegram to Jellyfin interaction Helper class.
/// </summary>
public class TelegramHelper
{
    /// <summary>
    ///     How old (in seconds) can authorization attempts be to be considered valid (compared to the auth_date field).
    /// </summary>
    private const long AllowedTimeOffset = 30;

    private readonly PluginConfiguration _config;
    private readonly ICryptoProvider _cryptoProvider;

    private readonly HMACSHA256 _hmac;
    private readonly TeleJellyPlugin _instance;
    private readonly ILogger _logger;

    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;

    private static readonly DateTime _unixStart = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    ///     Initializes a new instance of the <see cref="TelegramHelper" /> class.
    /// </summary>
    /// <param name="instance">of the Plugin.</param>
    /// <param name="sessionManager">for manual sign-in.</param>
    /// <param name="userManager">for getting and creating users.</param>
    /// <param name="cryptoProvider">for hashing passwords.</param>
    /// <param name="logger">for outputting errors.</param>
    internal TelegramHelper(TeleJellyPlugin instance, ISessionManager sessionManager, IUserManager userManager, ICryptoProvider cryptoProvider, ILogger logger)
    {
        _instance = instance;
        _config = instance.Configuration;

        _sessionManager = sessionManager;
        _userManager = userManager;
        _logger = logger;
        _cryptoProvider = cryptoProvider;

        using var sha256 = SHA256.Create();
        _hmac = new HMACSHA256(sha256.ComputeHash(Encoding.ASCII.GetBytes(_config.BotToken)));

        _logger.LogInformation("Telegram Helper initialized");
    }

    /// <summary>
    ///     Tries to find a Jellyfin user with the given Name.
    ///     If the user has no Username OR is not in any group -> Error.
    ///     If the user exists, check against his TG user id.
    ///     If the user does not exist, create him.
    ///     Update user details and save them.
    ///     return user.
    /// </summary>
    /// <param name="authData">verified Telegram user data.</param>
    /// <returns>Jellyfin User.</returns>
    /// <exception cref="ArgumentException">if User has no Username or Whitelist.</exception>
    /// <exception cref="InvalidOperationException">if User id has changed from last login.</exception>
    public async Task<User> GetOrCreateJellyUser(SortedDictionary<string, string> authData)
    {
        var userId = GetDictValue(authData, "id");
        var userName = GetDictValue(authData, "username");
        if (userId == null || userName == null)
        {
            throw new ArgumentException("Username not set.");
        }

        var isAdmin = _config.AdminUserNames.Any(admin => string.Equals(admin, userName, StringComparison.CurrentCultureIgnoreCase));

        // get user groups / whitelist
        var groups = _config.TelegramGroups;
        var userGroups = groups.Where(group => group.UserNames.Any(user => string.Equals(user, userName, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        if (!isAdmin && !userGroups.Any())
        {
            throw new ArgumentException("Username not whitelisted.");
        }

        // Actually get or create
        var user = _userManager.GetUserByName(userName);
        if (user == null)
        {
            _logger.LogInformation($"Telegram user '{userName}' doesn't exist, creating...");
            user = await _userManager.CreateUserAsync(userName).ConfigureAwait(false);

            // use a secure random password, can be changed later?
            var randBytes = new byte[128];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randBytes);
            user.Password = _cryptoProvider.CreatePasswordHash(Convert.ToBase64String(randBytes)).ToString();
        }

        // download user image from Telegram if given.
        var gotImageFromTelegram = await DownloadUserImage(user, authData);
        if (!gotImageFromTelegram)
        {
            var setDefaultImage = await SetDefaultUserImage(user);
            Debug.Assert(setDefaultImage, "Failed to set default image.");
        }

        // Update User Properties & Permissions etc.
        user.MaxActiveSessions = _config.MaxSessionCount;
        user.EnableAutoLogin = true;

        user.SetPermission(PermissionKind.IsAdministrator, isAdmin);

        var allFolderPerm = isAdmin || groups.Any(gr => gr.EnableAllFolders);
        user.SetPermission(PermissionKind.EnableAllFolders, allFolderPerm);
        if (!allFolderPerm)
        {
            var userFolders = userGroups.SelectMany(ug => ug.EnabledFolders).Distinct().ToArray();

            user.SetPreference(PreferenceKind.EnabledFolders, userFolders);
        }

        // save it if changed.
        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

        return user;
    }

    /// <summary>
    ///     Directly Log in the given Jellyfin User and return the result.
    /// </summary>
    /// <param name="request">to get User Client information.</param>
    /// <param name="user">to Authenticate.</param>
    /// <returns>JWT Token etc.</returns>
    public async Task<AuthenticationResult?> DoJellyUserAuth(HttpRequest request, User? user)
    {
        if (user == null)
        {
            return null;
        }

        var authRequest = new AuthenticationRequest
        {
            App = Constants.PluginName,
            AppVersion = GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0.1",
            DeviceId = request.Headers[HeaderNames.UserAgent].ToString(),
            DeviceName = "TelegramBrowserSSO",
            RemoteEndPoint = request.HttpContext.GetNormalizedRemoteIp().ToString(),
            UserId = user.Id,
            Username = user.Username
        };

        _logger.LogInformation("Auth request created...");

        return await _sessionManager.AuthenticateDirect(authRequest).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies the given user credentials with given hash by Bot Token.
    /// </summary>
    /// <param name="fields">unverified URL parameter data.</param>
    /// <exception cref="Exception">if data is invalid in any way.</exception>
    /// <returns>result.</returns>
    public TelegramAuthResult CheckTelegramAuthorizationImpl(SortedDictionary<string, string> fields)
    {
        Debug.Assert(fields != null, nameof(fields) + " != null");

        if (!fields.ContainsKey(Field.Id) ||
            !fields.TryGetValue(Field.AuthDate, out string? authDate) ||
            !fields.TryGetValue(Field.Hash, out string? hash))
        {
            return new TelegramAuthResult { ErrorMessage = "Data has missing fields." };
        }

        if (!long.TryParse(authDate, out long timestamp))
        {
            return new TelegramAuthResult { ErrorMessage = "Invalid AuthDate Format." };
        }

        if (Math.Abs(DateTime.UtcNow.Subtract(_unixStart).TotalSeconds - timestamp) > AllowedTimeOffset)
        {
            return new TelegramAuthResult { ErrorMessage = "Data is too old." };
        }

        if (hash is not { Length: 64 })
        {
            return new TelegramAuthResult { ErrorMessage = "Invalid hash." };
        }

        // Adapted from: https://stackoverflow.com/a/14333437/6845657
        var orderedKeys = fields.Keys.Where(k => !string.Equals("hash", k, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        var dataCheckString = string.Join("\n", orderedKeys.Select(key => $"{key}={fields[key]}"));
        var signature = _hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));

        if (signature.Where(FailCheckPredicate).Any())
        {
            return new TelegramAuthResult { ErrorMessage = "Invalid hash." };
        }

        return new TelegramAuthResult { Ok = true };

        bool FailCheckPredicate(byte t, int i)
        {
            if (hash[i * 2] != 87 + (signature[i] >> 4) + ((((signature[i] >> 4) - 10) >> 31) & -39))
            {
                return true;
            }

            return hash[(i * 2) + 1] != 87 + (signature[i] & 0xF) + ((((signature[i] & 0xF) - 10) >> 31) & -39);
        }
    }

    /// <summary>
    ///     Gets the "FQDN" of the current web request context (aka. this Jellyfin server's host address).
    ///     TODO: properly change the "ForcedUrlScheme".
    /// </summary>
    /// <param name="request">Incoming Context.</param>
    /// <returns>string of Format "FQDN.TLD".</returns>
    public string GetRequestBase(HttpRequest request)
    {
        var requestPort = request.Host.Port ?? -1;
        var requestScheme = _config.ForceUrlScheme ? _config.ForcedUrlScheme : request.Scheme;

        // strip the default ports of given protocol in the final result (80 = http, 443 = https)
        if ((requestPort == 80 && string.Equals(requestScheme, "http", StringComparison.OrdinalIgnoreCase)) || (requestPort == 443 && string.Equals(requestScheme, "https", StringComparison.OrdinalIgnoreCase)))
        {
            requestPort = -1;
        }

        return new UriBuilder { Scheme = requestScheme, Host = request.Host.Host, Port = requestPort, Path = request.PathBase }.ToString().TrimEnd('/');
    }

    /// <summary>
    ///     Small helper for finding the Value for a key, ignoring the CaSiNg Of ThE sTrInG.
    /// </summary>
    /// <typeparam name="T">value Type.</typeparam>
    /// <param name="dataDictionary">string indexed data.</param>
    /// <param name="key">to search for.</param>
    /// <returns>found value or null.</returns>
    public static T? GetDictValue<T>(IDictionary<string, T>? dataDictionary, string key)
    {
        var foundKey = dataDictionary?.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.CurrentCultureIgnoreCase));
        return foundKey != null ? dataDictionary![foundKey] : default;
    }

    /// <summary>
    ///     Function which tries to download a Telegram user image for the given jellyfin user.
    /// </summary>
    /// <param name="user">To set the Profile picture for.</param>
    /// <param name="authData">To download from Telegram.</param>
    /// <returns>whether image was successfully downloaded and set.</returns>
    public async Task<bool> DownloadUserImage(User user, SortedDictionary<string, string> authData)
    {
        var userPhotoUrl = GetDictValue(authData, "photo_url");
        if (userPhotoUrl == null)
        {
            return true;
        }

        var cleanedUrl = HttpUtility.UrlDecode(userPhotoUrl);

        var userImgPath = Path.Combine(_instance.ApplicationPaths.PluginsPath, Constants.PluginName, Constants.PluginDataFolder, Constants.UserImageFolder);
        _logger.LogDebug("Trying to download image for '{Username}' into '{UserImgPath}'", user.Username, userImgPath);

        try
        {
            if (!Directory.Exists(userImgPath))
            {
                Directory.CreateDirectory(userImgPath);
            }

            var userImgFile = Path.Combine(userImgPath, $"{user.Username}.jpg");

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(3);

            // Download photoUrl to a new file in "userImgPath".
            using (var response = await httpClient.GetAsync(cleanedUrl))
            using (var content = response.Content)
            {
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to download user image for {Username} from {PhotoUrl}. StatusCode: {StatusCode}", user.Username, cleanedUrl, response.StatusCode);
                    return false;
                }

                await using var stream = await content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(userImgFile, FileMode.Create);
                await stream.CopyToAsync(fileStream);
            }

            // If download succeeds, update user image and return true.
            if (user.ProfileImage == null)
            {
                user.ProfileImage = new ImageInfo(userImgFile);
            }
            else
            {
                user.ProfileImage.Path = userImgFile;
                user.ProfileImage.LastModified = DateTime.UtcNow;
            }

            _logger.LogInformation("Successfully downloaded telegram image for '{Username}'.", user.Username);
            return true;
        }
        catch (Exception ex)
        {
            // Log error and return false.
            _logger.LogError(ex, "Failed to download telegram image for '{Username}' from '{PhotoUrl}'.", user.Username, cleanedUrl);
            return false;
        }
    }

    /// <summary>
    ///     Try to set the default user image from embedded resources.
    /// </summary>
    /// <param name="user">Jellyfin Telegram User.</param>
    /// <returns>Whether action was successful.</returns>
    public async Task<bool> SetDefaultUserImage(User user)
    {
        var view = _instance.GetExtraFiles().FirstOrDefault(extra => extra.Name == Constants.DefaultUserImageExtraFile);
        if (view == null)
        {
            _logger.LogError("Failed to get DefaultUserImageExtraFile {Resource}", Constants.DefaultUserImageExtraFile);
            return false;
        }

        var stream = _instance.GetType().Assembly.GetManifestResourceStream(view.EmbeddedResourcePath);
        if (stream == null)
        {
            _logger.LogError("Failed to get resource {Resource}", view.EmbeddedResourcePath);
            return false;
        }

        var userImgPath = Path.Combine(_instance.ApplicationPaths.PluginsPath, Constants.PluginName, Constants.PluginDataFolder, Constants.UserImageFolder);
        _logger.LogDebug("Trying to save default image for '{Username}' into '{UserImgPath}'", user.Username, userImgPath);

        try
        {
            if (!Directory.Exists(userImgPath))
            {
                Directory.CreateDirectory(userImgPath);
            }

            var userImgFile = Path.Combine(userImgPath, $"{user.Username}.jpg");

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(3);

            // Copy Resource file stream to User image location on disk.
            await using var fileStream = new FileStream(userImgFile, FileMode.Create);
            await stream.CopyToAsync(fileStream);

            // If download succeeds, update user image and return true.
            if (user.ProfileImage == null)
            {
                user.ProfileImage = new ImageInfo(userImgFile);
            }
            else
            {
                user.ProfileImage.Path = userImgFile;
                user.ProfileImage.LastModified = DateTime.UtcNow;
            }

            _logger.LogInformation("Successfully set default telegram user image for '{Username}'.", user.Username);
            return true;
        }
        catch (Exception ex)
        {
            // Log error and return false.
            _logger.LogError(ex, "Failed to set default telegram user image for '{Username}' from '{PhotoUrl}'.", user.Username, view.EmbeddedResourcePath);
            return false;
        }
    }

    private static class Field
    {
        public const string AuthDate = "auth_date";
        public const string Id = "id";
        public const string Hash = "hash";
    }
}
