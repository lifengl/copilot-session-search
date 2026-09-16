#nullable enable

using System.Collections;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CopilotSessionSearch.Controls;

namespace CopilotSessionSearch.Tests;

public sealed class SelectableMarkdownViewerTests
{
    [Fact]
    public void ViewerRendersSelectableMarkdownAndHighlightsSearchText()
    {
        RunOnSta(
            () =>
            {
                const string markdown =
                    """
                    ## Already addressed

                    | Improvement | PR | Status |
                    |---|---|---|
                    | Symbol resolution | [770399](https://example.test/770399) | Draft |

                    ### Remaining improvements

                    1. Expand a known `diag session`.
                    """;
                var viewer = new SelectableMarkdownViewer
                {
                    SourceMarkdown = markdown,
                    HighlightText = "770399",
                };

                Assert.True(viewer.IsSelectionEnabled);
                Assert.NotNull(viewer.Document);

                string renderedText = new TextRange(
                    viewer.Document.ContentStart,
                    viewer.Document.ContentEnd).Text;

                Assert.Contains("Already addressed", renderedText, StringComparison.Ordinal);
                Assert.Contains("Improvement", renderedText, StringComparison.Ordinal);
                Assert.Contains("770399", renderedText, StringComparison.Ordinal);
                Assert.Contains("diag session", renderedText, StringComparison.Ordinal);
                Assert.NotEmpty(EnumerateElements<Table>(viewer.Document));
                Assert.Contains(ApplicationCommands.Copy, viewer.ContextMenu.Items.Cast<object>());

                Run highlightedRun = Assert.Single(
                    EnumerateRuns(viewer.Document),
                    run => run.Text.Contains("770399", StringComparison.Ordinal));
                int matchStart = highlightedRun.Text.IndexOf("770399", StringComparison.Ordinal);
                TextPointer? highlightPosition = highlightedRun.ContentStart.GetPositionAtOffset(
                    matchStart + 1,
                    LogicalDirection.Forward);
                TextPointer? highlightEnd = highlightPosition?.GetPositionAtOffset(
                    1,
                    LogicalDirection.Forward);

                Assert.NotNull(highlightPosition);
                Assert.NotNull(highlightEnd);
                Assert.Equal(
                    SystemColors.HighlightBrush,
                    new TextRange(highlightPosition, highlightEnd).GetPropertyValue(
                        TextElement.BackgroundProperty));
            });
    }

    private static IEnumerable<Run> EnumerateRuns(DependencyObject parent)
    {
        return EnumerateElements<Run>(parent);
    }

    private static IEnumerable<T> EnumerateElements<T>(DependencyObject parent)
        where T : DependencyObject
    {
        IEnumerable children = LogicalTreeHelper.GetChildren(parent);
        foreach (object child in children)
        {
            if (child is T match)
            {
                yield return match;
            }

            if (child is DependencyObject dependencyObject)
            {
                foreach (T descendant in EnumerateElements<T>(dependencyObject))
                {
                    yield return descendant;
                }
            }
        }
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
