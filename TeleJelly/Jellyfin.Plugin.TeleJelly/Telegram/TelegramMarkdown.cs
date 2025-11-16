using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.TeleJelly.Telegram;

/// <summary>
///     Helper methods for safely building Telegram MarkdownV2 messages.
/// </summary>
internal static class TelegramMarkdown
{
    // Characters that must be escaped in MarkdownV2 text context
    // See: https://core.telegram.org/bots/api#markdownv2-style
    private static readonly char[] _markdownV2SpecialChars =
    [
        '_', '*', '[', ']', '(', ')', '~', '`', '>', '#',
        '+', '-', '=', '|', '{', '}', '.', '!'
    ];

    /// <summary>
    ///     Escapes all MarkdownV2 special characters in a text string.
    ///     Use this for any user / dynamic content inserted into MarkdownV2.
    /// </summary>
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var sb = new StringBuilder(text.Length * 2);

        foreach (var ch in text)
        {
            if (_markdownV2SpecialChars.Contains(ch))
            {
                sb.Append('\\');
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
