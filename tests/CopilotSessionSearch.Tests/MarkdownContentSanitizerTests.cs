#nullable enable

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using CopilotSessionSearch.Controls;
using MdXaml;

namespace CopilotSessionSearch.Tests;

public sealed class MarkdownContentSanitizerTests
{
    [Fact]
    public void SanitizeTurnsMarkdownImagesIntoPlainText()
    {
        string sanitized = MarkdownContentSanitizer.Sanitize(
            "Before ![diagram](https://example.test/image.png) after.");

        Assert.Equal(
            "Before Image: diagram after.",
            sanitized);
        Assert.DoesNotContain(
            "https://example.test/image.png",
            sanitized,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("![nested [alternate] text](https://example.test/nested.png)")]
    [InlineData("![line one\nline two](https://example.test/multiline.png)")]
    [InlineData("\\![escaped](https://example.test/escaped.png)")]
    [InlineData("![escaped\\](https://example.test/escaped-alt.png)")]
    [InlineData("!\u001a[normalized](https://example.test/normalized.png)")]
    [InlineData("![reference][image-ref]\n\n[image-ref]: https://example.test/reference.png")]
    [InlineData("![collapsed][]\n\n[collapsed]: https://example.test/collapsed.png")]
    [InlineData("![shortcut]\n\n[shortcut]: https://example.test/shortcut.png")]
    public void SanitizeRemovesAllMarkdownImageForms(
        string markdown)
    {
        string sanitized =
            MarkdownContentSanitizer.Sanitize(markdown);

        Assert.DoesNotContain(
            "![",
            sanitized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizePreventsPlaceholderFromCreatingAnotherImage()
    {
        string sanitized = MarkdownContentSanitizer.Sanitize(
            "![caption!](https://example.test/removed.png)[x](https://example.test/link)");

        Assert.Contains(
            "Image: caption&#33;",
            sanitized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "![",
            sanitized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "removed.png",
            sanitized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizePreservesTextAfterQuotedTitleParenthesis()
    {
        string sanitized = MarkdownContentSanitizer.Sanitize(
            "![alt](image.png \"title(\") KEEP THIS TEXT) suffix");

        Assert.Equal(
            "Image: alt KEEP THIS TEXT) suffix",
            sanitized);
    }

    [Fact]
    public void SanitizePreservesOuterLinkAroundImageAsTextLink()
    {
        string sanitized = MarkdownContentSanitizer.Sanitize(
            "[![build](https://example.test/badge.png)](https://example.test/details)");

        Assert.Equal(
            "[Image: build](https://example.test/details)",
            sanitized);
        Assert.DoesNotContain(
            "badge.png",
            sanitized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeEscapesHtmlImageElements()
    {
        string sanitized = MarkdownContentSanitizer.Sanitize(
            "Before <img src=\"https://example.test/image.png\"> after.");

        Assert.Contains("&lt;img", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizedComplexImagesRenderWithoutImageElements()
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    string sanitized =
                        MarkdownContentSanitizer.Sanitize(
                            """
                            [![build](https://example.test/badge.png)](https://example.test/details)

                            ![nested [alternate] text](https://example.test/nested.png)

                            ![line one
                            line two](https://example.test/multiline.png)
                            """);
                    var markdown = new Markdown
                    {
                        DisabledLazyLoad = true,
                    };

                    FlowDocument document =
                        markdown.Transform(sanitized);

                    Assert.DoesNotContain(
                        EnumerateDescendants(document),
                        descendant => descendant is Image);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void SanitizeManyUnmatchedImageMarkersCompletesQuickly()
    {
        string markdown = string.Concat(
            Enumerable.Repeat(
                "![\n",
                128_000));
        var stopwatch = Stopwatch.StartNew();

        string sanitized =
            MarkdownContentSanitizer.Sanitize(markdown);

        stopwatch.Stop();
        Assert.DoesNotContain(
            "![",
            sanitized,
            StringComparison.Ordinal);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Sanitizing unmatched markers took {stopwatch.Elapsed}.");
    }

    private static IEnumerable<DependencyObject> EnumerateDescendants(
        DependencyObject parent)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is not DependencyObject dependencyObject)
            {
                continue;
            }

            yield return dependencyObject;
            foreach (DependencyObject descendant
                in EnumerateDescendants(dependencyObject))
            {
                yield return descendant;
            }
        }
    }
}
