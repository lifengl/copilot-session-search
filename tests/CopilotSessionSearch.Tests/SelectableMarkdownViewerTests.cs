#nullable enable

using System.Collections;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CopilotSessionSearch.Controls;
using ICSharpCode.AvalonEdit;

namespace CopilotSessionSearch.Tests;

public sealed class SelectableMarkdownViewerTests
{
    [Fact]
    public void ViewerUsesPlainCodeInDarkModeAndRestoresHighlightingInLightMode()
    {
        RunOnSta(
            () =>
            {
                const string markdown =
                    """
                    ```csharp
                    // ADDED: routes cached-state projects
                    Verify.Operation(buildContext is not null);
                    ```
                    """;
                var viewer = new SelectableMarkdownViewer
                {
                    Background = Brushes.Black,
                    Foreground = Brushes.White,
                    SourceMarkdown = markdown,
                };
                FlowDocument document = Assert.IsType<FlowDocument>(viewer.Document);
                TextEditor editor = Assert.Single(
                    EnumerateElements<TextEditor>(document));

                Assert.Null(editor.SyntaxHighlighting);
                Assert.Equal(Brushes.White, editor.Foreground);
                Assert.Equal(Brushes.White, editor.TextArea.Foreground);
                Assert.Equal(SystemColors.HighlightBrush, editor.TextArea.SelectionBrush);
                Assert.Equal(
                    SystemColors.HighlightTextBrush,
                    editor.TextArea.SelectionForeground);

                viewer.Foreground = Brushes.Black;

                Assert.NotNull(editor.SyntaxHighlighting);
                Assert.Equal(Brushes.Black, editor.Foreground);

                viewer.Foreground = Brushes.White;

                Assert.Null(editor.SyntaxHighlighting);
                Assert.Equal(Brushes.White, editor.Foreground);
            });
    }

    [Fact]
    public void CopyWholeMessageAddsRichFormatsAndMarkdownText()
    {
        RunOnSta(
            () =>
            {
                const string markdown =
                    """
                    ## Heading

                    | Name | Value |
                    |---|---|
                    | Alpha | One |
                    """;
                var viewer = new SelectableMarkdownViewer
                {
                    SourceMarkdown = markdown,
                };
                PrepareViewer(viewer);
                FlowDocument document = Assert.IsType<FlowDocument>(viewer.Document);
                TextSelection selection = Assert.IsType<TextSelection>(viewer.Selection);
                Run heading = Assert.Single(
                    EnumerateRuns(document),
                    run => run.Text == "Heading");
                selection.Select(heading.ContentStart, heading.ContentEnd);

                IDataObject dataObject = CaptureCopyData(
                    viewer,
                    viewer.CopyWholeMessage);

                Assert.Equal(
                    markdown,
                    dataObject.GetData(
                        DataFormats.UnicodeText,
                        autoConvert: false));
                Assert.Equal(
                    markdown,
                    dataObject.GetData(
                        DataFormats.Text,
                        autoConvert: false));
                Assert.Equal(
                    markdown,
                    dataObject.GetData(
                        DataFormats.StringFormat,
                        autoConvert: false));
                string html = Assert.IsType<string>(
                    dataObject.GetData(
                        DataFormats.Html,
                        autoConvert: false));
                Assert.Contains("<h2>Heading</h2>", html, StringComparison.Ordinal);
                Assert.Contains("<table ", html, StringComparison.Ordinal);
                Assert.Contains("Alpha", html, StringComparison.Ordinal);
                Assert.Equal("Heading", selection.Text);
            });
    }

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

    [Fact]
    public void ViewerHighlightsCaseInsensitiveRegularExpressionMatches()
    {
        RunOnSta(
            () =>
            {
                var viewer = new SelectableMarkdownViewer
                {
                    HighlightText = "hot ?reload",
                    SourceMarkdown = "HotReload and hot reload.",
                    UseRegularExpression = true,
                };
                FlowDocument document = Assert.IsType<FlowDocument>(viewer.Document);
                IReadOnlyList<Run> runs = EnumerateRuns(document).ToArray();
                Run hotReload = Assert.Single(
                    runs,
                    candidate => candidate.Text == "HotReload");
                Run hotReloadWithSpace = Assert.Single(
                    runs,
                    candidate => candidate.Text == "hot reload");

                AssertHighlighted(hotReload, "HotReload");
                AssertHighlighted(hotReloadWithSpace, "hot reload");
            });
    }

    [Fact]
    public void CopyAddsWordCompatibleHtmlAndPreservesExistingFormats()
    {
        RunOnSta(
            () =>
            {
                const string markdown =
                    """
                    ## Heading

                    This has **bold**, *italic*, a [link](https://example.test), and `inline code`.

                    1. First
                    2. Second

                    | Name | Value |
                    |---|---|
                    | Alpha | One |
                    | Beta | Two |
                    """;
                var viewer = new SelectableMarkdownViewer
                {
                    SourceMarkdown = markdown,
                };

                IDataObject dataObject = CopyAllWithoutUsingClipboard(viewer);

                Assert.True(
                    dataObject.GetDataPresent(
                        DataFormats.Text,
                        autoConvert: false));
                Assert.True(
                    dataObject.GetDataPresent(
                        DataFormats.Rtf,
                        autoConvert: false));
                Assert.True(
                    dataObject.GetDataPresent(
                        DataFormats.Xaml,
                        autoConvert: false));
                Assert.True(
                    dataObject.GetDataPresent(
                        DataFormats.Html,
                        autoConvert: false));

                string html = Assert.IsType<string>(
                    dataObject.GetData(
                        DataFormats.Html,
                        autoConvert: false));
                Assert.Contains("<h2>Heading</h2>", html, StringComparison.Ordinal);
                Assert.Contains("<strong>bold</strong>", html, StringComparison.Ordinal);
                Assert.Contains("<em>italic</em>", html, StringComparison.Ordinal);
                Assert.Contains(
                    "<a href=\"https://example.test/\">link</a>",
                    html,
                    StringComparison.Ordinal);
                Assert.Contains("<code ", html, StringComparison.Ordinal);
                Assert.Contains(">inline code</code>", html, StringComparison.Ordinal);
                Assert.Contains("<ol>", html, StringComparison.Ordinal);
                Assert.Contains(
                    "<li><div style=\"margin:0\">First</div></li>",
                    html,
                    StringComparison.Ordinal);
                Assert.Contains("<table ", html, StringComparison.Ordinal);
                Assert.Contains("<th ", html, StringComparison.Ordinal);
                Assert.Contains(
                    ">Name</div></th>",
                    html,
                    StringComparison.Ordinal);
                Assert.Contains("<td ", html, StringComparison.Ordinal);
                Assert.Contains(
                    ">Alpha</div></td>",
                    html,
                    StringComparison.Ordinal);
            });
    }

    private static IDataObject CopyAllWithoutUsingClipboard(
        SelectableMarkdownViewer viewer)
    {
        PrepareViewer(viewer);
        FlowDocument document = Assert.IsType<FlowDocument>(viewer.Document);
        TextSelection selection = Assert.IsType<TextSelection>(viewer.Selection);
        selection.Select(document.ContentStart, document.ContentEnd);

        return CaptureCopyData(viewer, viewer.CopySelection);
    }

    private static IDataObject CaptureCopyData(
        SelectableMarkdownViewer viewer,
        Func<bool> copy)
    {
        IDataObject? copiedData = null;
        DataObjectCopyingEventHandler handler = (_, eventArgs) =>
        {
            copiedData = eventArgs.DataObject;
            eventArgs.CancelCommand();
        };
        DataObject.AddCopyingHandler(viewer, handler);

        try
        {
            Assert.True(copy());
        }
        finally
        {
            DataObject.RemoveCopyingHandler(viewer, handler);
        }

        return Assert.IsAssignableFrom<IDataObject>(copiedData);
    }

    private static void PrepareViewer(SelectableMarkdownViewer viewer)
    {
        viewer.ApplyTemplate();
        viewer.Measure(new Size(800, 600));
        viewer.Arrange(new Rect(0, 0, 800, 600));
        viewer.UpdateLayout();
    }

    [Fact]
    public void CopySelectionReturnsFalseWithoutSelectedText()
    {
        RunOnSta(
            () =>
            {
                var viewer = new SelectableMarkdownViewer
                {
                    SourceMarkdown = "Nothing is selected.",
                };
                viewer.ApplyTemplate();
                viewer.Measure(new Size(800, 600));
                viewer.Arrange(new Rect(0, 0, 800, 600));
                viewer.UpdateLayout();

                Assert.False(viewer.CopySelection());
            });
    }

    private static void AssertHighlighted(Run run, string text)
    {
        int matchStart = run.Text.IndexOf(text, StringComparison.Ordinal);
        TextPointer? highlightPosition = run.ContentStart.GetPositionAtOffset(
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
