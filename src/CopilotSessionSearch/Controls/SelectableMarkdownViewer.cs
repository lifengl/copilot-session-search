#nullable enable

using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MdXaml;

namespace CopilotSessionSearch.Controls;

public sealed class SelectableMarkdownViewer : MarkdownScrollViewer
{
    private const string CodeSpanTag = "CodeSpan";

    private bool _isApplyingDocumentTheme;

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

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (!_isApplyingDocumentTheme
            && (e.Property == ForegroundProperty || e.Property == BackgroundProperty)
            && Document is FlowDocument document)
        {
            ApplyDocumentTheme(document);
            ApplySearchHighlighting(document);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ListViewItem? item = FindVisualAncestor<ListViewItem>(this);
            if (item is not null)
            {
                item.Focus();
                e.Handled = true;
                return;
            }
        }

        if (Keyboard.Modifiers == ModifierKeys.None
            && IsListNavigationKey(e.Key))
        {
            ListView? listView = FindVisualAncestor<ListView>(this);
            ListViewItem? item = FindVisualAncestor<ListViewItem>(this);
            if (listView is not null && item is not null)
            {
                item.Focus();
                RaiseKeyOnList(listView, e);
                e.Handled = true;
                return;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        ScrollViewer? parentScrollViewer = FindVisualAncestor<ScrollViewer>(this);
        if (parentScrollViewer is not null)
        {
            e.Handled = true;
            var forwardedEvent = new MouseWheelEventArgs(
                e.MouseDevice,
                e.Timestamp,
                e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source = parentScrollViewer,
            };

            parentScrollViewer.RaiseEvent(forwardedEvent);
            return;
        }

        base.OnPreviewMouseWheel(e);
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
        document.Background = Brushes.Transparent;
        BindingOperations.SetBinding(
            document,
            TextElement.FontFamilyProperty,
            new Binding(nameof(FontFamily))
            {
                Mode = BindingMode.OneWay,
                Source = this,
            });
        BindingOperations.SetBinding(
            document,
            TextElement.FontSizeProperty,
            new Binding(nameof(FontSize))
            {
                Mode = BindingMode.OneWay,
                Source = this,
            });
        BindingOperations.SetBinding(
            document,
            TextElement.ForegroundProperty,
            new Binding(nameof(Foreground))
            {
                Mode = BindingMode.OneWay,
                Source = this,
            });

        ApplyDocumentTheme(document);
        ApplySearchHighlighting(document);
    }

    private void ApplyDocumentTheme(FlowDocument document)
    {
        if (_isApplyingDocumentTheme)
        {
            return;
        }

        _isApplyingDocumentTheme = true;
        try
        {
            Brush foreground = Foreground ?? SystemColors.WindowTextBrush;
            Brush subtleBackground = CreateTranslucentBrush(
                foreground,
                SystemParameters.HighContrast ? (byte)0 : (byte)24);
            Brush borderBrush = CreateTranslucentBrush(
                foreground,
                SystemParameters.HighContrast ? (byte)255 : (byte)72);

            var textElements = new List<TextElement>();
            CollectElements(document, textElements);
            foreach (TextElement textElement in textElements)
            {
                bool isCodeSpan = string.Equals(
                    textElement.Tag as string,
                    CodeSpanTag,
                    StringComparison.Ordinal);

                if (isCodeSpan)
                {
                    FontFamily fontFamily = textElement.FontFamily;
                    double fontSize = textElement.FontSize;
                    FontStretch fontStretch = textElement.FontStretch;
                    FontStyle fontStyle = textElement.FontStyle;
                    FontWeight fontWeight = textElement.FontWeight;

                    textElement.Style = null;
                    textElement.FontFamily = fontFamily;
                    textElement.FontSize = fontSize;
                    textElement.FontStretch = fontStretch;
                    textElement.FontStyle = fontStyle;
                    textElement.FontWeight = fontWeight;
                }

                BindingOperations.SetBinding(
                    textElement,
                    TextElement.ForegroundProperty,
                    new Binding(nameof(Foreground))
                    {
                        Mode = BindingMode.OneWay,
                        Source = this,
                    });

                object localBackground = textElement.ReadLocalValue(
                    TextElement.BackgroundProperty);
                if (isCodeSpan
                    || (localBackground != DependencyProperty.UnsetValue
                        && localBackground is not null))
                {
                    textElement.Background = subtleBackground;
                }
            }

            var tables = new List<Table>();
            CollectElements(document, tables);
            foreach (Table table in tables)
            {
                table.Background = Brushes.Transparent;
                table.BorderBrush = borderBrush;

                foreach (TableRowGroup rowGroup in table.RowGroups)
                {
                    rowGroup.Background = Brushes.Transparent;

                    for (int rowIndex = 0; rowIndex < rowGroup.Rows.Count; rowIndex++)
                    {
                        TableRow row = rowGroup.Rows[rowIndex];
                        row.Background = Brushes.Transparent;

                        foreach (TableCell cell in row.Cells)
                        {
                            cell.Background = rowIndex == 0
                                ? subtleBackground
                                : Brushes.Transparent;
                            cell.BorderBrush = borderBrush;
                        }
                    }
                }
            }

            var controls = new List<Control>();
            CollectElements(document, controls);
            foreach (Control control in controls)
            {
                if (control.GetType().Namespace?.StartsWith(
                    "ICSharpCode.AvalonEdit",
                    StringComparison.Ordinal) == true)
                {
                    control.Background = subtleBackground;
                    control.BorderBrush = borderBrush;
                    control.Foreground = foreground;
                }
            }
        }
        finally
        {
            _isApplyingDocumentTheme = false;
        }
    }

    private void ApplySearchHighlighting(FlowDocument document)
    {
        string highlightText = HighlightText ?? string.Empty;
        if (highlightText.Length > 0)
        {
            var runs = new List<Run>();
            CollectElements(document, runs);

            foreach (Run run in runs)
            {
                HighlightRun(run, highlightText);
            }
        }
    }

    private static void CollectElements<T>(
        DependencyObject parent,
        ICollection<T> elements)
        where T : DependencyObject
    {
        IEnumerable children = LogicalTreeHelper.GetChildren(parent);
        foreach (object child in children)
        {
            if (child is T match)
            {
                elements.Add(match);
            }

            if (child is DependencyObject dependencyObject)
            {
                CollectElements(dependencyObject, elements);
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

    private static Brush CreateTranslucentBrush(Brush source, byte alpha)
    {
        if (alpha == 0)
        {
            return Brushes.Transparent;
        }

        if (source is not SolidColorBrush solidColorBrush)
        {
            return source;
        }

        Color color = solidColorBrush.Color;
        color.A = alpha;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static bool IsListNavigationKey(Key key)
    {
        return key is Key.Up
            or Key.Down
            or Key.PageUp
            or Key.PageDown
            or Key.Home
            or Key.End;
    }

    private static void RaiseKeyOnList(ListView listView, KeyEventArgs originalEvent)
    {
        PresentationSource? presentationSource = PresentationSource.FromVisual(listView);
        if (presentationSource is null)
        {
            return;
        }

        var forwardedEvent = new KeyEventArgs(
            originalEvent.KeyboardDevice,
            presentationSource,
            originalEvent.Timestamp,
            originalEvent.Key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            Source = listView,
        };

        listView.RaiseEvent(forwardedEvent);
    }

    private static T? FindVisualAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        DependencyObject? current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

}
