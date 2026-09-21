#nullable enable

using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using MdXaml;

namespace CopilotSessionSearch.Controls;

public sealed class SelectableMarkdownViewer : MarkdownScrollViewer
{
    private const string CodeSpanTag = "CodeSpan";

    private readonly ConditionalWeakTable<TextEditor, CodeBlockThemeState> _codeBlockThemes = new();
    private bool _isApplyingDocumentTheme;
    private bool _isCopyingWholeMessage;

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

    public static readonly DependencyProperty MatchWholeWordProperty = DependencyProperty.Register(
        nameof(MatchWholeWord),
        typeof(bool),
        typeof(SelectableMarkdownViewer),
        new FrameworkPropertyMetadata(false, OnMarkdownPropertyChanged));

    public static readonly DependencyProperty IsCaseSensitiveProperty = DependencyProperty.Register(
        nameof(IsCaseSensitive),
        typeof(bool),
        typeof(SelectableMarkdownViewer),
        new FrameworkPropertyMetadata(false, OnMarkdownPropertyChanged));

    public static readonly DependencyProperty UseRegularExpressionProperty = DependencyProperty.Register(
        nameof(UseRegularExpression),
        typeof(bool),
        typeof(SelectableMarkdownViewer),
        new FrameworkPropertyMetadata(false, OnMarkdownPropertyChanged));

    public SelectableMarkdownViewer()
    {
        ClickAction = ClickAction.SafetyOpenBrowser;
        DataObject.AddCopyingHandler(this, OnCopying);
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

    public bool MatchWholeWord
    {
        get => (bool)GetValue(MatchWholeWordProperty);
        set => SetValue(MatchWholeWordProperty, value);
    }

    public bool IsCaseSensitive
    {
        get => (bool)GetValue(IsCaseSensitiveProperty);
        set => SetValue(IsCaseSensitiveProperty, value);
    }

    public bool UseRegularExpression
    {
        get => (bool)GetValue(UseRegularExpressionProperty);
        set => SetValue(UseRegularExpressionProperty, value);
    }

    public bool CopySelection()
    {
        if (Selection is not TextSelection selection
            || selection.IsEmpty
            || !ApplicationCommands.Copy.CanExecute(null, this))
        {
            return false;
        }

        ApplicationCommands.Copy.Execute(null, this);
        return true;
    }

    public bool CopyWholeMessage()
    {
        if (Document is not FlowDocument document
            || Selection is not TextSelection selection)
        {
            return false;
        }

        TextPointer selectionStart = selection.Start;
        TextPointer selectionEnd = selection.End;
        _isCopyingWholeMessage = true;
        try
        {
            selection.Select(document.ContentStart, document.ContentEnd);
            if (!ApplicationCommands.Copy.CanExecute(null, this))
            {
                return false;
            }

            ApplicationCommands.Copy.Execute(null, this);
            return true;
        }
        finally
        {
            _isCopyingWholeMessage = false;
            selection.Select(selectionStart, selectionEnd);
        }
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

    private static void OnCopying(
        object sender,
        DataObjectCopyingEventArgs eventArgs)
    {
        var viewer = (SelectableMarkdownViewer)sender;
        if (viewer._isCopyingWholeMessage)
        {
            SetWholeMessageClipboardFormats(
                viewer,
                eventArgs.DataObject);
            return;
        }

        if (!eventArgs.DataObject.GetDataPresent(
            DataFormats.Html,
            autoConvert: false))
        {
            string? xaml = eventArgs.DataObject.GetData(
                DataFormats.Xaml,
                autoConvert: false) as string;
            if (!string.IsNullOrWhiteSpace(xaml))
            {
                string htmlFragment = WpfXamlToHtmlConverter.Convert(xaml);
                eventArgs.DataObject.SetData(
                    DataFormats.Html,
                    HtmlClipboardFormat.Create(htmlFragment),
                    autoConvert: false);
            }
        }
    }

    private static void SetWholeMessageClipboardFormats(
        SelectableMarkdownViewer viewer,
        IDataObject dataObject)
    {
        string sourceMarkdown =
            viewer.SourceMarkdown ?? string.Empty;
        FlowDocument richDocument =
            CreateSerializableDocument(
                viewer,
                sourceMarkdown);
        string xaml = SaveTextRange(
            richDocument,
            DataFormats.Xaml,
            Encoding.UTF8);
        string htmlFragment =
            WpfXamlToHtmlConverter.Convert(xaml);
        string rtf = SaveTextRange(
            richDocument,
            DataFormats.Rtf,
            Encoding.ASCII);

        dataObject.SetData(
            DataFormats.Html,
            HtmlClipboardFormat.Create(htmlFragment),
            autoConvert: false);
        dataObject.SetData(
            DataFormats.Rtf,
            rtf,
            autoConvert: false);
        dataObject.SetData(
            DataFormats.Xaml,
            xaml,
            autoConvert: false);
        dataObject.SetData(
            DataFormats.Text,
            sourceMarkdown,
            autoConvert: false);
        dataObject.SetData(
            DataFormats.UnicodeText,
            sourceMarkdown,
            autoConvert: false);
        dataObject.SetData(
            DataFormats.StringFormat,
            sourceMarkdown,
            autoConvert: false);
    }

    private static string SaveTextRange(
        FlowDocument document,
        string dataFormat,
        Encoding encoding)
    {
        using var stream = new MemoryStream();
        new TextRange(
            document.ContentStart,
            document.ContentEnd).Save(
                stream,
                dataFormat);
        return encoding.GetString(stream.ToArray());
    }

    private static FlowDocument CreateSerializableDocument(
        SelectableMarkdownViewer viewer,
        string sourceMarkdown)
    {
        FlowDocument document = viewer.Engine.Transform(
            MarkdownContentSanitizer.Sanitize(sourceMarkdown));
        document.FontFamily = viewer.FontFamily;
        document.FontSize = viewer.FontSize;
        ApplySerializableHeadingSizes(
            document.Blocks,
            viewer.FontSize);
        ReplaceCodeBlocks(document.Blocks);
        return document;
    }

    private static void ApplySerializableHeadingSizes(
        BlockCollection blocks,
        double baseFontSize)
    {
        foreach (Block block in blocks)
        {
            if (block is Paragraph paragraph
                && paragraph.Tag is string tag)
            {
                double? scale = tag switch
                {
                    "Heading1" => 2,
                    "Heading2" => 1.6,
                    "Heading3" => 1.4,
                    "Heading4" => 1.25,
                    "Heading5" => 1.1,
                    _ => null,
                };
                if (scale is double headingScale)
                {
                    paragraph.FontSize =
                        baseFontSize * headingScale;
                }
                else if (string.Equals(
                    tag,
                    "Heading6",
                    StringComparison.Ordinal))
                {
                    paragraph.FontWeight =
                        FontWeights.Bold;
                }
            }

            switch (block)
            {
                case Section section:
                    ApplySerializableHeadingSizes(
                        section.Blocks,
                        baseFontSize);
                    break;

                case List list:
                    foreach (ListItem item in list.ListItems)
                    {
                        ApplySerializableHeadingSizes(
                            item.Blocks,
                            baseFontSize);
                    }
                    break;

                case Table table:
                    foreach (TableRowGroup rowGroup
                        in table.RowGroups)
                    {
                        foreach (TableRow row in rowGroup.Rows)
                        {
                            foreach (TableCell cell in row.Cells)
                            {
                                ApplySerializableHeadingSizes(
                                    cell.Blocks,
                                    baseFontSize);
                            }
                        }
                    }
                    break;
            }
        }
    }

    private static void ReplaceCodeBlocks(
        BlockCollection blocks)
    {
        foreach (Block block in blocks.Cast<Block>().ToArray())
        {
            switch (block)
            {
                case BlockUIContainer container
                    when container.Child is TextEditor editor:
                    var paragraph = new Paragraph
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Tag = "CodeBlock",
                        Margin = new Thickness(0),
                    };
                    string[] lines = editor.Text
                        .Replace(
                            "\r\n",
                            "\n",
                            StringComparison.Ordinal)
                        .Replace('\r', '\n')
                        .Split('\n');
                    for (int index = 0;
                        index < lines.Length;
                        index++)
                    {
                        if (index > 0)
                        {
                            paragraph.Inlines.Add(
                                new LineBreak());
                        }

                        if (lines[index].Length > 0)
                        {
                            paragraph.Inlines.Add(
                                new Run(lines[index]));
                        }
                    }

                    blocks.InsertBefore(
                        container,
                        paragraph);
                    blocks.Remove(container);
                    break;

                case Section section:
                    ReplaceCodeBlocks(section.Blocks);
                    break;

                case List list:
                    foreach (ListItem item in list.ListItems
                        .Cast<ListItem>()
                        .ToArray())
                    {
                        ReplaceCodeBlocks(item.Blocks);
                    }
                    break;

                case Table table:
                    foreach (TableRowGroup rowGroup
                        in table.RowGroups)
                    {
                        foreach (TableRow row in rowGroup.Rows)
                        {
                            foreach (TableCell cell in row.Cells)
                            {
                                ReplaceCodeBlocks(cell.Blocks);
                            }
                        }
                    }
                    break;
            }
        }
    }

    private void RenderMarkdown()
    {
        string sourceMarkdown = SourceMarkdown ?? string.Empty;
        try
        {
            Markdown = MarkdownContentSanitizer.Sanitize(
                sourceMarkdown);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or XamlParseException)
        {
            Document = new FlowDocument(
                new Paragraph(
                    new Run(sourceMarkdown)));
        }

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
            bool usePlainCode =
                SystemParameters.HighContrast || IsLightForeground(foreground);

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

                if (isCodeSpan
                    || HasVisibleBackground(textElement.Background))
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

            var codeEditors = new List<TextEditor>();
            CollectElements(document, codeEditors);
            foreach (TextEditor codeEditor in codeEditors)
            {
                CodeBlockThemeState state = _codeBlockThemes.GetValue(
                    codeEditor,
                    static editor => new CodeBlockThemeState(editor.SyntaxHighlighting));

                codeEditor.SyntaxHighlighting = usePlainCode
                    ? null
                    : state.SyntaxHighlighting;
                codeEditor.Background = subtleBackground;
                codeEditor.BorderBrush = borderBrush;
                codeEditor.Foreground = foreground;
                codeEditor.LineNumbersForeground = foreground;
                codeEditor.TextArea.Background = Brushes.Transparent;
                codeEditor.TextArea.Foreground = foreground;
                codeEditor.TextArea.SelectionBrush = SystemColors.HighlightBrush;
                codeEditor.TextArea.SelectionForeground = SystemColors.HighlightTextBrush;
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
            var options = new SessionSearchOptions(
                MatchWholeWord: MatchWholeWord,
                IsCaseSensitive: IsCaseSensitive,
                UseRegularExpression: UseRegularExpression);
            TextSearchPattern pattern = TextSearchPattern.Create(
                highlightText,
                options);
            var matchesByRun =
                new List<(Run Run, IReadOnlyList<TextMatch> Matches)>(runs.Count);

            try
            {
                foreach (Run run in runs)
                {
                    matchesByRun.Add((
                        run,
                        pattern.FindMatches(run.Text)));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return;
            }

            foreach ((Run run, IReadOnlyList<TextMatch> matches) in matchesByRun)
            {
                HighlightRun(run, matches);
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

    private static void HighlightRun(
        Run run,
        IReadOnlyList<TextMatch> matches)
    {
        for (int index = matches.Count - 1; index >= 0; index--)
        {
            TextMatch match = matches[index];
            TextPointer? start = run.ContentStart.GetPositionAtOffset(
                match.Start,
                LogicalDirection.Forward);
            TextPointer? end = run.ContentStart.GetPositionAtOffset(
                match.End,
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

    private static bool HasVisibleBackground(Brush? background)
    {
        return background is not null
            && (background is not SolidColorBrush solidColorBrush
                || solidColorBrush.Color.A > 0);
    }

    private static bool IsLightForeground(Brush foreground)
    {
        if (foreground is not SolidColorBrush solidColorBrush)
        {
            return false;
        }

        Color color = solidColorBrush.Color;
        double luminance =
            (0.2126 * color.R) +
            (0.7152 * color.G) +
            (0.0722 * color.B);
        return luminance >= 160;
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

    private sealed record CodeBlockThemeState(
        IHighlightingDefinition? SyntaxHighlighting);
}
