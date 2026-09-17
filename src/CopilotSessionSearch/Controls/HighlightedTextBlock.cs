#nullable enable

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

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

    public static readonly DependencyProperty MatchWholeWordProperty = DependencyProperty.Register(
        nameof(MatchWholeWord),
        typeof(bool),
        typeof(HighlightedTextBlock),
        new FrameworkPropertyMetadata(false, OnTextPropertyChanged));

    public static readonly DependencyProperty IsCaseSensitiveProperty = DependencyProperty.Register(
        nameof(IsCaseSensitive),
        typeof(bool),
        typeof(HighlightedTextBlock),
        new FrameworkPropertyMetadata(false, OnTextPropertyChanged));

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

        var options = new SessionSearchOptions(MatchWholeWord, IsCaseSensitive);
        IReadOnlyList<int> matches = LiteralTextMatcher.FindMatches(
            sourceText,
            highlightText,
            options);
        int contentStart = 0;

        foreach (int matchStart in matches)
        {
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

        if (contentStart < sourceText.Length)
        {
            Inlines.Add(new Run(sourceText[contentStart..]));
        }
    }
}
