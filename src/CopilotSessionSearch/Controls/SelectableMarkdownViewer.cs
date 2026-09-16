#nullable enable

using System.Collections;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MdXaml;

namespace CopilotSessionSearch.Controls;

public sealed class SelectableMarkdownViewer : MarkdownScrollViewer
{
    public static readonly DependencyProperty SourceMarkdownProperty = DependencyProperty.Register(
        nameof(SourceMarkdown),
        typeof(string),
        typeof(SelectableMarkdownViewer),
        new FrameworkPropertyMetadata(string.Empty, OnMarkdownPropertyChanged));

    public static readonly DependencyProperty HighlightTextProperty = DependencyProperty.Register(
        nameof(HighlightText),
        typeof(string),
        typeof(SelectableMarkdownViewer),
        new FrameworkPropertyMetadata(string.Empty, OnMarkdownPropertyChanged));

    public SelectableMarkdownViewer()
    {
        ClickAction = ClickAction.SafetyOpenBrowser;
        IsSelectionEnabled = true;
        IsToolBarVisible = false;
    }

    public string SourceMarkdown
    {
        get => (string)GetValue(SourceMarkdownProperty);
        set => SetValue(SourceMarkdownProperty, value);
    }

    public string HighlightText
    {
        get => (string)GetValue(HighlightTextProperty);
        set => SetValue(HighlightTextProperty, value);
    }

    private static void OnMarkdownPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var viewer = (SelectableMarkdownViewer)dependencyObject;
        viewer.RenderMarkdown();
    }

    private void RenderMarkdown()
    {
        Markdown = MarkdownContentSanitizer.Sanitize(SourceMarkdown ?? string.Empty);

        FlowDocument? document = Document;
        if (document is null)
        {
            return;
        }

        document.PagePadding = new Thickness(0);

        string highlightText = HighlightText ?? string.Empty;
        if (highlightText.Length > 0)
        {
            var runs = new List<Run>();
            CollectRuns(document, runs);

            foreach (Run run in runs)
            {
                HighlightRun(run, highlightText);
            }
        }
    }

    private static void CollectRuns(
        DependencyObject parent,
        ICollection<Run> runs)
    {
        IEnumerable children = LogicalTreeHelper.GetChildren(parent);
        foreach (object child in children)
        {
            if (child is Run run)
            {
                runs.Add(run);
            }
            else if (child is DependencyObject dependencyObject)
            {
                CollectRuns(dependencyObject, runs);
            }
        }
    }

    private static void HighlightRun(Run run, string highlightText)
    {
        string text = run.Text;
        var matchStarts = new List<int>();
        int searchStart = 0;

        while (searchStart <= text.Length - highlightText.Length)
        {
            int matchStart = text.IndexOf(
                highlightText,
                searchStart,
                StringComparison.OrdinalIgnoreCase);

            if (matchStart < 0)
            {
                break;
            }

            matchStarts.Add(matchStart);
            searchStart = matchStart + highlightText.Length;
        }

        for (int index = matchStarts.Count - 1; index >= 0; index--)
        {
            int matchStart = matchStarts[index];
            TextPointer? start = run.ContentStart.GetPositionAtOffset(
                matchStart,
                LogicalDirection.Forward);
            TextPointer? end = run.ContentStart.GetPositionAtOffset(
                matchStart + highlightText.Length,
                LogicalDirection.Forward);

            if (start is not null && end is not null)
            {
                var range = new TextRange(start, end);
                range.ApplyPropertyValue(TextElement.BackgroundProperty, SystemColors.HighlightBrush);
                range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
                range.ApplyPropertyValue(TextElement.ForegroundProperty, SystemColors.HighlightTextBrush);
            }
        }
    }
}
