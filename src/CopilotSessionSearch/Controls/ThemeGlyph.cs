#nullable enable

using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Controls;

public sealed class ThemeGlyph : FrameworkElement
{
    private const double DesignSize = 16;

    public static readonly DependencyProperty PreferenceProperty = DependencyProperty.Register(
        nameof(Preference),
        typeof(AppThemePreference),
        typeof(ThemeGlyph),
        new FrameworkPropertyMetadata(
            AppThemePreference.System,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(
            typeof(ThemeGlyph),
            new FrameworkPropertyMetadata(
                SystemColors.ControlTextBrush,
                FrameworkPropertyMetadataOptions.AffectsRender
                    | FrameworkPropertyMetadataOptions.Inherits));

    public AppThemePreference Preference
    {
        get => (AppThemePreference)GetValue(PreferenceProperty);
        set => SetValue(PreferenceProperty, value);
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

        var pen = new Pen(Foreground, 1.5)
        {
            EndLineCap = PenLineCap.Round,
            StartLineCap = PenLineCap.Round,
        };

        switch (Preference)
        {
            case AppThemePreference.System:
                DrawSystem(drawingContext, pen);
                break;

            case AppThemePreference.Light:
                DrawLight(drawingContext, pen);
                break;

            case AppThemePreference.Dark:
                DrawDark(drawingContext);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(Preference));
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    private static void DrawSystem(DrawingContext drawingContext, Pen pen)
    {
        drawingContext.DrawRoundedRectangle(
            null,
            pen,
            new Rect(1.75, 2.5, 12.5, 8.5),
            1,
            1);
        drawingContext.DrawLine(pen, new Point(8, 11), new Point(8, 13.25));
        drawingContext.DrawLine(pen, new Point(5.25, 13.25), new Point(10.75, 13.25));
    }

    private static void DrawLight(DrawingContext drawingContext, Pen pen)
    {
        drawingContext.DrawEllipse(null, pen, new Point(8, 8), 2.75, 2.75);

        Point[] innerPoints =
        [
            new Point(8, 1.25),
            new Point(12.77, 3.23),
            new Point(14.75, 8),
            new Point(12.77, 12.77),
            new Point(8, 14.75),
            new Point(3.23, 12.77),
            new Point(1.25, 8),
            new Point(3.23, 3.23),
        ];
        Point[] outerPoints =
        [
            new Point(8, 2.75),
            new Point(11.71, 4.29),
            new Point(13.25, 8),
            new Point(11.71, 11.71),
            new Point(8, 13.25),
            new Point(4.29, 11.71),
            new Point(2.75, 8),
            new Point(4.29, 4.29),
        ];

        for (int index = 0; index < innerPoints.Length; index++)
        {
            drawingContext.DrawLine(pen, innerPoints[index], outerPoints[index]);
        }
    }

    private void DrawDark(DrawingContext drawingContext)
    {
        var outer = new EllipseGeometry(new Point(7.1, 8), 5.4, 5.4);
        var inner = new EllipseGeometry(new Point(9.75, 5.85), 5.2, 5.2);
        var crescent = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            outer,
            inner);
        drawingContext.DrawGeometry(Foreground, null, crescent);
    }
}
