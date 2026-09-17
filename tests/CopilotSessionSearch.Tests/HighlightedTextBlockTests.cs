#nullable enable

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Documents;
using CopilotSessionSearch.Controls;

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
