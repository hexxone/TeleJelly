using System;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.TeleJelly.Classes;

internal static class ControllerExtensions
{
    /// <summary>
    ///     Gets the "FQDN" of the current web request context (aka. this Jellyfin server's host address).
    ///     With respect to the configured "ForcedUrlScheme".
    /// </summary>
    /// <param name="request">Incoming Context.</param>
    /// <param name="configuration">of the plugin.</param>
    /// <returns>string of Format "FQDN.TLD".</returns>
    public static string GetRequestBase(this HttpRequest request, PluginConfiguration configuration)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request), "Request is null.");
        }

        var configSchema = configuration.ForcedUrlScheme;

        var requestPort = request.Host.Port ?? -1;
        var requestScheme = (string.Equals(configSchema, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(configSchema, "https", StringComparison.OrdinalIgnoreCase)) ? configSchema : request.Scheme;

        // strip the default ports of given protocol in the final result (80 = http, 443 = https)
        if ((requestPort == 80 && string.Equals(requestScheme, "http", StringComparison.OrdinalIgnoreCase)) || (requestPort == 443 && string.Equals(requestScheme, "https", StringComparison.OrdinalIgnoreCase)))
        {
            requestPort = -1;
        }

        return new UriBuilder { Scheme = requestScheme, Host = request.Host.Host, Port = requestPort, Path = request.PathBase }.ToString().TrimEnd('/');
    }
}
