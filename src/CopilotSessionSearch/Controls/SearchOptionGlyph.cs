#nullable enable

using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CopilotSessionSearch.Controls;

public enum SearchOptionGlyphKind
{
    WholeWord,
    CaseSensitive,
    RegularExpression,
    AiSearch,
}

public sealed class SearchOptionGlyph : FrameworkElement
{
    private const double DesignSize = 16;

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind),
            typeof(SearchOptionGlyphKind),
            typeof(SearchOptionGlyph),
            new FrameworkPropertyMetadata(
                SearchOptionGlyphKind.WholeWord,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(
            typeof(SearchOptionGlyph),
            new FrameworkPropertyMetadata(
                SystemColors.ControlTextBrush,
                FrameworkPropertyMetadataOptions.AffectsRender
                    | FrameworkPropertyMetadataOptions.Inherits));

    public SearchOptionGlyphKind Kind
    {
        get => (SearchOptionGlyphKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            double.IsInfinity(availableSize.Width)
                ? DesignSize
                : Math.Min(DesignSize, availableSize.Width),
            double.IsInfinity(availableSize.Height)
                ? DesignSize
                : Math.Min(DesignSize, availableSize.Height));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        double offsetX = (ActualWidth - size) / 2;
        double offsetY = (ActualHeight - size) / 2;
        double scale = size / DesignSize;

        drawingContext.PushTransform(new TranslateTransform(offsetX, offsetY));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        switch (Kind)
        {
            case SearchOptionGlyphKind.WholeWord:
                DrawWholeWord(drawingContext);
                break;

            case SearchOptionGlyphKind.CaseSensitive:
                DrawText(
                    drawingContext,
                    "Aa",
                    "Segoe UI",
                    10,
                    FontWeights.SemiBold);
                break;

            case SearchOptionGlyphKind.RegularExpression:
                DrawText(
                    drawingContext,
                    ".*",
                    "Consolas",
                    11,
                    FontWeights.Bold);
                break;

            case SearchOptionGlyphKind.AiSearch:
                DrawAiSearch(drawingContext);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(Kind));
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    private void DrawWholeWord(DrawingContext drawingContext)
    {
        var pen = new Pen(Foreground, 1.25);
        drawingContext.DrawLine(
            pen,
            new Point(1.5, 2.5),
            new Point(1.5, 13.5));
        drawingContext.DrawLine(
            pen,
            new Point(14.5, 2.5),
            new Point(14.5, 13.5));
        DrawText(
            drawingContext,
            "ab",
            "Segoe UI",
            8.5,
            FontWeights.SemiBold);
    }

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(fontFamily),
                FontStyles.Normal,
                fontWeight,
                FontStretches.Normal),
            fontSize,
            Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(
            formattedText,
            new Point(
                (DesignSize - formattedText.Width) / 2,
                (DesignSize - formattedText.Height) / 2));
    }

    private void DrawAiSearch(DrawingContext drawingContext)
    {
        DrawSparkle(
            drawingContext,
            new Point(7, 8.5),
            outerRadius: 6,
            innerRadius: 1.35);
        DrawSparkle(
            drawingContext,
            new Point(13, 3),
            outerRadius: 2,
            innerRadius: 0.5);
    }

    private void DrawSparkle(
        DrawingContext drawingContext,
        Point center,
        double outerRadius,
        double innerRadius)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(
                new Point(center.X, center.Y - outerRadius),
                isFilled: true,
                isClosed: true);
            context.LineTo(
                new Point(
                    center.X + innerRadius,
                    center.Y - innerRadius),
                isStroked: true,
                isSmoothJoin: true);
            context.LineTo(
                new Point(center.X + outerRadius, center.Y),
                isStroked: true,
                isSmoothJoin: true);
            context.LineTo(
                new Point(
                    center.X + innerRadius,
                    center.Y + innerRadius),
                isStroked: true,
                isSmoothJoin: true);
            context.LineTo(
                new Point(center.X, center.Y + outerRadius),
                isStroked: true,
                isSmoothJoin: true);
            context.LineTo(
                new Point(
                    center.X - innerRadius,
                    center.Y + innerRadius),
                isStroked: true,
                isSmoothJoin: true);
            context.LineTo(
                new Point(center.X - outerRadius, center.Y),
                isStroked: true,
                isSmoothJoin: true);
            context.LineTo(
                new Point(
                    center.X - innerRadius,
                    center.Y - innerRadius),
                isStroked: true,
                isSmoothJoin: true);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(Foreground, null, geometry);
    }
}
