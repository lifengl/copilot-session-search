#nullable enable

using System.Text;
using System.Text.RegularExpressions;

namespace CopilotSessionSearch.Controls;

public static partial class MarkdownContentSanitizer
{
    private static readonly TimeSpan ImagePatternTimeout =
        TimeSpan.FromSeconds(1);

    private static readonly Regex MarkdownImagePattern =
        CreateMarkdownImagePattern();

    public static string Sanitize(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        string normalized = markdown.Replace(
            "\u001a",
            string.Empty,
            StringComparison.Ordinal);
        string withoutImages;
        try
        {
            withoutImages = MarkdownImagePattern.Replace(
                normalized,
                match =>
                    CreateImagePlaceholder(
                        match.Groups["alt"].Value));
        }
        catch (RegexMatchTimeoutException)
        {
            withoutImages = normalized;
        }

        string neutralized =
            NeutralizeRemainingImageMarkers(withoutImages);
        return HtmlImagePattern().Replace(
            neutralized,
            "&lt;img");
    }

    private static Regex CreateMarkdownImagePattern()
    {
        const int MaximumNestingDepth = 6;
        string nestedBrackets = Repeat(
                """
                (?>
                    [^\[\]]+
                  |
                    \[
                """,
                MaximumNestingDepth)
            + Repeat(
                """
                    \]
                )*
                """,
                MaximumNestingDepth);
        string nestedParentheses = Repeat(
                """
                (?>
                    [^()\n\t]+?
                  |
                    \(
                """,
                MaximumNestingDepth)
            + Repeat(
                """
                    \)
                )*?
                """,
                MaximumNestingDepth);
        string pattern =
            $$"""
            !
            \[
                (?<alt>{{nestedBrackets}})
            \]
            \(
                [ ]*
                (?<url>{{nestedParentheses}})
                [ ]*
                (?:
                    (?<quote>['"])
                    (?<title>.*?)
                    \k<quote>
                    [ ]*
                )?
            \)
            """;
        return new Regex(
            pattern,
            RegexOptions.Compiled
                | RegexOptions.CultureInvariant
                | RegexOptions.IgnorePatternWhitespace
                | RegexOptions.Singleline,
            ImagePatternTimeout);
    }

    private static string NeutralizeRemainingImageMarkers(
        string markdown)
    {
        int firstMarker = markdown.IndexOf(
            "![",
            StringComparison.Ordinal);
        if (firstMarker < 0)
        {
            return markdown;
        }

        var result = new StringBuilder(
            markdown.Length + 16);
        int copyStart = 0;
        int marker = firstMarker;
        while (marker >= 0)
        {
            result.Append(
                markdown,
                copyStart,
                marker - copyStart);
            result.Append("&#33;&#91;");
            copyStart = marker + 2;
            marker = markdown.IndexOf(
                "![",
                copyStart,
                StringComparison.Ordinal);
        }

        result.Append(
            markdown,
            copyStart,
            markdown.Length - copyStart);
        return result.ToString();
    }

    private static string CreateImagePlaceholder(
        string alternateText)
    {
        string normalized = WhitespacePattern()
            .Replace(alternateText, " ")
            .Trim()
            .Replace('[', '(')
            .Replace(']', ')')
            .Replace(
                "!",
                "&#33;",
                StringComparison.Ordinal);
        return normalized.Length == 0
            ? "Image"
            : $"Image: {normalized}";
    }

    private static string Repeat(
        string value,
        int count)
    {
        var result = new StringBuilder(
            value.Length * count);
        for (int index = 0; index < count; index++)
        {
            result.Append(value);
        }
        return result.ToString();
    }

    [GeneratedRegex(
        @"<\s*img\b",
        RegexOptions.CultureInvariant
            | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImagePattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
