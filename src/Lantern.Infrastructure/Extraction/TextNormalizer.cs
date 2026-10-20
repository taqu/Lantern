using System.Text;
using System.Text.RegularExpressions;

namespace Lantern.Infrastructure.Extraction;

public static class TextNormalizer
{
    // Normalize line endings, remove invalid control chars, clean whitespace
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Normalize line endings to LF
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                sb.Append('\n');
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++; // Skip \n in \r\n
            }
            else if (IsInvalidControlChar(c))
            {
                // Skip invalid control chars (but keep \n, \t)
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // Remove excessive blank lines (3+ blank lines -> 2)
    public static string CollapseBlankLines(string text)
    {
        return Regex.Replace(text, @"\n{3,}", "\n\n");
    }

    // Trim individual lines of trailing whitespace
    public static string TrimLineEnds(string text)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd();
        return string.Join('\n', lines);
    }

    private static bool IsInvalidControlChar(char c)
    {
        // Keep printable chars, \n, \t, and all Unicode above ASCII control range
        if (c == '\n' || c == '\t') return false;
        if (c < 0x20) return true;   // ASCII control chars (except \n, \t)
        if (c == 0x7F) return true;  // DEL
        return false;
    }

    public static string NormalizeAll(string text)
    {
        text = Normalize(text);
        text = TrimLineEnds(text);
        text = CollapseBlankLines(text);
        return text.Trim();
    }
}
