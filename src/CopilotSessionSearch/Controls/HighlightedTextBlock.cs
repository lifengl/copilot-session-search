#nullable enable

using System.Text.RegularExpressions;
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

    public static readonly DependencyProperty UseRegularExpressionProperty = DependencyProperty.Register(
        nameof(UseRegularExpression),
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

    public bool UseRegularExpression
    {
        get => (bool)GetValue(UseRegularExpressionProperty);
        set => SetValue(UseRegularExpressionProperty, value);
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

        var options = new SessionSearchOptions(
            MatchWholeWord: MatchWholeWord,
            IsCaseSensitive: IsCaseSensitive,
            UseRegularExpression: UseRegularExpression);
        TextSearchPattern pattern = TextSearchPattern.Create(
            highlightText,
            options);
        IReadOnlyList<TextMatch> matches;
        try
        {
            matches = pattern.FindMatches(sourceText);
        }
        catch (RegexMatchTimeoutException)
        {
            Inlines.Add(new Run(sourceText));
            return;
        }

        int contentStart = 0;

        foreach (TextMatch match in matches)
        {
            if (match.Start > contentStart)
            {
                Inlines.Add(new Run(sourceText[contentStart..match.Start]));
            }

            Inlines.Add(
                new Run(sourceText.Substring(match.Start, match.Length))
                {
                    Background = SystemColors.HighlightBrush,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = SystemColors.HighlightTextBrush,
                });

            contentStart = match.End;
        }

        if (contentStart < sourceText.Length)
        {
            Inlines.Add(new Run(sourceText[contentStart..]));
        }
    }
}
