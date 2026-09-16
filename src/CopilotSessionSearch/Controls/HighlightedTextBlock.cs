#nullable enable

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CopilotSessionSearch.Controls;

public sealed class HighlightedTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
        nameof(SourceText),
        typeof(string),
        typeof(HighlightedTextBlock),
        new FrameworkPropertyMetadata(string.Empty, OnTextPropertyChanged));

    public static readonly DependencyProperty HighlightTextProperty = DependencyProperty.Register(
        nameof(HighlightText),
        typeof(string),
        typeof(HighlightedTextBlock),
        new FrameworkPropertyMetadata(string.Empty, OnTextPropertyChanged));

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public string HighlightText
    {
        get => (string)GetValue(HighlightTextProperty);
        set => SetValue(HighlightTextProperty, value);
    }

    private static void OnTextPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var textBlock = (HighlightedTextBlock)dependencyObject;
        textBlock.RebuildInlines();
    }

    private void RebuildInlines()
    {
        Inlines.Clear();

        string sourceText = SourceText ?? string.Empty;
        string highlightText = HighlightText ?? string.Empty;
        if (sourceText.Length == 0)
        {
            return;
        }

        if (highlightText.Length == 0)
        {
            Inlines.Add(new Run(sourceText));
            return;
        }

        int contentStart = 0;
        while (contentStart < sourceText.Length)
        {
            int matchStart = sourceText.IndexOf(
                highlightText,
                contentStart,
                StringComparison.OrdinalIgnoreCase);

            if (matchStart < 0)
            {
                Inlines.Add(new Run(sourceText[contentStart..]));
                break;
            }

            if (matchStart > contentStart)
            {
                Inlines.Add(new Run(sourceText[contentStart..matchStart]));
            }

            Inlines.Add(
                new Run(sourceText.Substring(matchStart, highlightText.Length))
                {
                    Background = SystemColors.HighlightBrush,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = SystemColors.HighlightTextBrush,
                });

            contentStart = matchStart + highlightText.Length;
        }
    }
}
