#nullable enable

using CopilotSessionSearch.Controls;

namespace CopilotSessionSearch.Tests;

public sealed class MarkdownContentSanitizerTests
{
    [Fact]
    public void SanitizeTurnsMarkdownImagesIntoLinks()
    {
        string sanitized = MarkdownContentSanitizer.Sanitize(
            "Before ![diagram](https://example.test/image.png) after.");

        Assert.Equal(
            "Before [Image: diagram](https://example.test/image.png) after.",
            sanitized);
    }

    [Fact]
    public void SanitizeEscapesHtmlImageElements()
    {
        string sanitized = MarkdownContentSanitizer.Sanitize(
            "Before <img src=\"https://example.test/image.png\"> after.");

        Assert.Contains("&lt;img", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", sanitized, StringComparison.OrdinalIgnoreCase);
    }
}
