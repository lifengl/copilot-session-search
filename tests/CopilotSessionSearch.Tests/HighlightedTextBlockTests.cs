#nullable enable

using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using CopilotSessionSearch.Controls;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class HighlightedTextBlockTests
{
    [Fact]
    public void HighlightingUsesWholeWordAndCaseSensitiveOptions()
    {
        RunOnSta(
            () =>
            {
                var textBlock = new HighlightedTextBlock
                {
                    HighlightText = "cat",
                    IsCaseSensitive = true,
                    MatchWholeWord = true,
                    SourceText = "cat category CAT cat_cat",
                    UseRegularExpression = false,
                };

                IReadOnlyList<Run> highlightedRuns = textBlock.Inlines
                    .OfType<Run>()
                    .Where(run => Equals(run.Background, SystemColors.HighlightBrush))
                    .ToArray();

                Run highlightedRun = Assert.Single(highlightedRuns);
                Assert.Equal("cat", highlightedRun.Text);
            });
    }

    [Fact]
    public void RegularExpressionHighlightingUsesActualMatchLengthsAndIgnoresCase()
    {
        RunOnSta(
            () =>
            {
                var textBlock = new HighlightedTextBlock
                {
                    HighlightText = "hot ?reload",
                    IsCaseSensitive = false,
                    MatchWholeWord = false,
                    SourceText = "HotReload and hot reload.",
                    UseRegularExpression = true,
                };

                IReadOnlyList<string> highlightedText = textBlock.Inlines
                    .OfType<Run>()
                    .Where(run => Equals(run.Background, SystemColors.HighlightBrush))
                    .Select(run => run.Text)
                    .ToArray();

                Assert.Equal(["HotReload", "hot reload"], highlightedText);
            });
    }

    [Fact]
    public void RegularExpressionTimeoutFallsBackToUnhighlightedText()
    {
        RunOnSta(
            () =>
            {
                const string expression = "^(a+)+(?=b)$";
                string sourceText = new string('a', 50_000) + "c";
                TextSearchPattern pattern = TextSearchPattern.Create(
                    expression,
                    new SessionSearchOptions(
                        MatchWholeWord: false,
                        IsCaseSensitive: true,
                        UseRegularExpression: true));
                Assert.Throws<RegexMatchTimeoutException>(
                    () => pattern.FindMatches(sourceText));

                var textBlock = new HighlightedTextBlock
                {
                    HighlightText = expression,
                    IsCaseSensitive = true,
                    MatchWholeWord = false,
                    UseRegularExpression = true,
                    SourceText = sourceText,
                };

                Run run = Assert.Single(
                    textBlock.Inlines.OfType<Run>());
                Assert.Equal(sourceText, run.Text);
                Assert.NotEqual(
                    SystemColors.HighlightBrush,
                    run.Background);
            });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
