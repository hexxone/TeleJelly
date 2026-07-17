using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.TeleJelly.Classes.Models;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

internal static class DownloadLibrarySelection
{
    internal sealed record LibraryChoice(Guid Id, string Name, string CollectionType);

    internal static IReadOnlyList<LibraryChoice> GetLibraries(ILibraryManager libraryManager)
    {
        var virtualFolders = libraryManager.GetVirtualFolders()
            .Select(folder =>
            {
                var idText = Convert.ToString(folder.ItemId, CultureInfo.InvariantCulture);
                return Guid.TryParse(idText, out var id)
                    ? new LibraryChoice(
                        id,
                        string.IsNullOrWhiteSpace(folder.Name) ? "Unnamed Library" : folder.Name,
                        Convert.ToString(folder.CollectionType, CultureInfo.InvariantCulture) ?? string.Empty)
                    : null;
            })
            .Where(choice => choice != null)
            .Cast<LibraryChoice>()
            .ToArray();

        if (virtualFolders.Length > 0)
        {
            return virtualFolders;
        }

        // Older/restored Jellyfin installations can temporarily expose no virtual-folder
        // metadata. Preserve the existing picker by falling back to root children.
        return libraryManager.GetUserRootFolder().Children
            .Select(item => new LibraryChoice(item.Id, item.Name ?? "Unnamed Library", string.Empty))
            .ToArray();
    }

    internal static LibraryChoice? SelectAutomaticLibrary(
        IReadOnlyList<LibraryChoice> libraries,
        MediaType mediaType)
    {
        if (libraries.Count == 1)
        {
            return libraries[0];
        }

        var expectedType = mediaType switch
        {
            MediaType.Movie => "movies",
            MediaType.Series => "tvshows",
            _ => null
        };
        if (expectedType == null)
        {
            return null;
        }

        var compatible = libraries
            .Where(library => string.Equals(
                library.CollectionType,
                expectedType,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return compatible.Length == 1 ? compatible[0] : null;
    }

    internal static IReadOnlyList<LibraryChoice> GetSelectableLibraries(
        IReadOnlyList<LibraryChoice> libraries,
        MediaType mediaType)
    {
        var expectedType = mediaType switch
        {
            MediaType.Movie => "movies",
            MediaType.Series => "tvshows",
            _ => null
        };
        if (expectedType == null)
        {
            return libraries;
        }

        var compatible = libraries
            .Where(library => string.Equals(
                library.CollectionType,
                expectedType,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return compatible.Length > 0 ? compatible : libraries;
    }
}
