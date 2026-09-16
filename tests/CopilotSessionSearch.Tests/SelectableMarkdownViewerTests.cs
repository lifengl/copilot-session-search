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
    public void ViewerNormalizesMarkdownColorsForDarkAndLightForegrounds()
    {
        RunOnSta(
            () =>
            {
                const string markdown =
                    """
                    The `dump`-investigation skills use the safe Cdb.MCP contract.
                    Live `g` waits use `timeoutMs: -1`, and invasive work relies on `-p`.

                    | Area | Decision |
                    |---|---|
                    | Repository | `Q:\ws\CopilotSessionSearch` |
                    | Framework | WPF |
                    """;
                var viewer = new SelectableMarkdownViewer
                {
                    Background = Brushes.Black,
                    Foreground = Brushes.White,
                    HighlightText = "dump",
                    SourceMarkdown = markdown,
                };
                FlowDocument document = Assert.IsType<FlowDocument>(viewer.Document);

                IReadOnlyList<Run> darkRuns = EnumerateRuns(document).ToArray();
                IReadOnlyList<TableCell> darkCells =
                    EnumerateElements<TableCell>(document).ToArray();
                IReadOnlyList<TableRow> darkRows =
                    EnumerateElements<TableRow>(document).ToArray();
                IReadOnlyList<TableRowGroup> darkRowGroups =
                    EnumerateElements<TableRowGroup>(document).ToArray();

                Assert.NotEmpty(darkRuns);
                Assert.NotEmpty(darkCells);
                Assert.NotEmpty(darkRows);
                Assert.NotEmpty(darkRowGroups);
                Assert.All(darkRuns, run => Assert.Equal(Brushes.White, run.Foreground));
                IReadOnlyList<Run> inlineCodeRuns = darkRuns
                    .Where(run => run.Tag as string == "CodeSpan")
                    .ToArray();
                Assert.True(inlineCodeRuns.Count >= 4);
                Assert.All(inlineCodeRuns, run => Assert.Null(run.Style));
                Assert.Contains(
                    inlineCodeRuns,
                    run => run.Text.Contains("g", StringComparison.Ordinal));
                Assert.Contains(
                    inlineCodeRuns,
                    run => run.Text.Contains("timeoutMs: -1", StringComparison.Ordinal));
                Assert.Contains(
                    inlineCodeRuns,
                    run => run.Text.Contains("-p", StringComparison.Ordinal));
                Run timeoutCode = Assert.Single(
                    darkRuns,
                    run => run.Text.Contains(
                        "timeoutMs: -1",
                        StringComparison.Ordinal));
                Assert.InRange(
                    Assert.IsType<SolidColorBrush>(timeoutCode.Background).Color.A,
                    (byte)1,
                    (byte)24);
                Assert.All(
                    darkCells,
                    cell => Assert.InRange(
                        Assert.IsType<SolidColorBrush>(cell.Background).Color.A,
                        (byte)0,
                        (byte)24));
                Assert.All(
                    darkRows,
                    row => Assert.InRange(GetAlpha(row.Background), (byte)0, (byte)24));
                Assert.All(
                    darkRowGroups,
                    rowGroup => Assert.InRange(
                        GetAlpha(rowGroup.Background),
                        (byte)0,
                        (byte)24));

                viewer.Foreground = Brushes.Black;

                IReadOnlyList<Run> lightRuns = EnumerateRuns(document).ToArray();
                Run highlightedRun = Assert.Single(
                    lightRuns,
                    run => run.Text.Contains("dump", StringComparison.Ordinal));
                Assert.Equal(SystemColors.HighlightBrush, highlightedRun.Background);
                Assert.Equal(SystemColors.HighlightTextBrush, highlightedRun.Foreground);
                Assert.All(
                    lightRuns.Where(run => !ReferenceEquals(run, highlightedRun)),
                    run => Assert.Equal(Brushes.Black, run.Foreground));

                viewer.Foreground = Brushes.White;
                viewer.HighlightText = "timeoutMs";
                FlowDocument highlightedDocument = Assert.IsType<FlowDocument>(viewer.Document);
                Run remainingCode = Assert.Single(
                    EnumerateRuns(highlightedDocument),
                    run => run.Text.Contains(": -1", StringComparison.Ordinal));
                Assert.InRange(
                    Assert.IsType<SolidColorBrush>(remainingCode.Background).Color.A,
                    (byte)1,
                    (byte)24);
            });
    }

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

    private static byte GetAlpha(Brush? brush)
    {
        return brush is SolidColorBrush solidColorBrush
            ? solidColorBrush.Color.A
            : (byte)0;
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
