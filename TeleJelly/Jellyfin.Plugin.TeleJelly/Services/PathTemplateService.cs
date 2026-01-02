using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.TeleJelly.Classes.Configuration;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TeleJelly.Services
{
    public class PathTemplateService
    {
        private readonly ILogger<PathTemplateService> _logger;

        public PathTemplateService(ILogger<PathTemplateService> logger)
        {
            _logger = logger;
        }

        public Task<DynamicPathVariable[]> ExtractDynamicVariablesAsync(string template, LibrarySettings library)
        {
            var dynamicVars = new List<DynamicPathVariable>();
            var regex = new Regex(@"\[(\w+)\]");
            var matches = regex.Matches(template);

            foreach (Match match in matches)
            {
                var varName = match.Groups[1].Value;
                var libraryVar = library.DynamicVariables.FirstOrDefault(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
                if (libraryVar != null)
                {
                    dynamicVars.Add(libraryVar);
                }
                else
                {
                    _logger.LogWarning("Dynamic variable '[{VarName}]' found in template but not defined in library settings.", varName);
                }
            }

            return Task.FromResult(dynamicVars.ToArray());
        }

        public Task<string> ApplyTemplateAsync(string template, ManagedDownload download, Dictionary<string, string> userVars, string originalFileName)
        {
            var sb = new StringBuilder(template);
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitize = new Func<string, string>(input => new string(input.Where(c => !invalidChars.Contains(c)).ToArray()).Trim());

            // Apply user-filled dynamic variables first
            foreach (var userVar in userVars)
            {
                sb.Replace($"[{userVar.Key}]", sanitize(userVar.Value));
            }

            // Apply static variables
            sb.Replace("{title}", sanitize(download.Title));
            sb.Replace("{year}", download.Year?.ToString() ?? string.Empty);
            sb.Replace("{imdbId}", download.ImdbId);
            sb.Replace("{filename}", sanitize(Path.GetFileNameWithoutExtension(originalFileName)));
            sb.Replace("{ext}", Path.GetExtension(originalFileName));

            // Handle formatted variables like {season:00}
            sb.Replace("{season:00}", download.Season?.ToString("00") ?? string.Empty);
            sb.Replace("{season}", download.Season?.ToString() ?? string.Empty);
            sb.Replace("{episode:00}", download.Episode?.ToString("00") ?? string.Empty);
            sb.Replace("{episode}", download.Episode?.ToString() ?? string.Empty);

            var resultPath = sb.ToString();

            // Clean up any remaining dynamic variables that weren't filled
            resultPath = Regex.Replace(resultPath, @"\[\w+\]", string.Empty).Trim();
            // Clean up empty directory separators that might result from missing optional values
            resultPath = resultPath.Replace(Path.DirectorySeparatorChar.ToString() + Path.DirectorySeparatorChar.ToString(), Path.DirectorySeparatorChar.ToString());

            _logger.LogInformation("Applied path template. Original: '{Template}', Result: '{ResultPath}'", template, resultPath);

            return Task.FromResult(resultPath);
        }

        public Task<bool> ValidatePathAsync(string path)
        {
            try
            {
                // This is a basic check. It doesn't check for path length or existence.
                var invalidPathChars = Path.GetInvalidPathChars();
                if (path.Any(c => invalidPathChars.Contains(c)))
                {
                    _logger.LogWarning("Path validation failed for '{Path}': Contains invalid path characters.", path);
                    return Task.FromResult(false);
                }

                var components = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var invalidFileChars = Path.GetInvalidFileNameChars();
                foreach (var component in components)
                {
                    if (component.Any(c => invalidFileChars.Contains(c)))
                    {
                        _logger.LogWarning("Path validation failed for '{Path}': Component '{Component}' contains invalid file name characters.", path, component);
                        return Task.FromResult(false);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occurred during path validation for '{Path}'", path);
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
    }
}
