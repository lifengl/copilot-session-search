#nullable enable

using System.Text.RegularExpressions;

namespace CopilotSessionSearch.Controls;

public static partial class MarkdownContentSanitizer
{
    public static string Sanitize(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        string sanitized = MarkdownImagePattern().Replace(
            markdown,
            match =>
            {
                string alternateText = match.Groups["alt"].Value;
                return string.IsNullOrWhiteSpace(alternateText)
                    ? "[Image]"
                    : $"[Image: {alternateText}]";
            });

        return HtmlImagePattern().Replace(sanitized, "&lt;img");
    }

    [GeneratedRegex(
        @"!\[(?<alt>[^\]\r\n]*)\](?=\s*(?:\(|\[))",
        RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownImagePattern();

    [GeneratedRegex(
        @"<\s*img\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImagePattern();
}
