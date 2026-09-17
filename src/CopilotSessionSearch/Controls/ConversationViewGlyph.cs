#nullable enable

using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace CopilotSessionSearch.Controls;

public sealed class ConversationViewGlyph : FrameworkElement
{
    private const double DesignSize = 16;

    public static readonly DependencyProperty IsWholeConversationProperty =
        DependencyProperty.Register(
            nameof(IsWholeConversation),
            typeof(bool),
            typeof(ConversationViewGlyph),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(
            typeof(ConversationViewGlyph),
            new FrameworkPropertyMetadata(
                SystemColors.ControlTextBrush,
                FrameworkPropertyMetadataOptions.AffectsRender
                    | FrameworkPropertyMetadataOptions.Inherits));

    public bool IsWholeConversation
    {
        get => (bool)GetValue(IsWholeConversationProperty);
        set => SetValue(IsWholeConversationProperty, value);
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
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
        };

        if (IsWholeConversation)
        {
            DrawWholeConversation(drawingContext, pen);
        }
        else
        {
            DrawMatchingMessages(drawingContext, pen);
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    private static void DrawMatchingMessages(
        DrawingContext drawingContext,
        Pen pen)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(
                new Point(2, 2.5),
                isFilled: false,
                isClosed: false);
            context.LineTo(new Point(14, 2.5), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(9.5, 7.75), isStroked: true, isSmoothJoin: true);
            context.LineTo(new Point(9.5, 12.25), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(6.5, 14), isStroked: true, isSmoothJoin: true);
            context.LineTo(new Point(6.5, 7.75), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(2, 2.5), isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private void DrawWholeConversation(
        DrawingContext drawingContext,
        Pen pen)
    {
        double[] rowCenters = [3.5, 8, 12.5];
        foreach (double rowCenter in rowCenters)
        {
            drawingContext.DrawEllipse(
                Foreground,
                null,
                new Point(2.5, rowCenter),
                1,
                1);
            drawingContext.DrawLine(
                pen,
                new Point(5, rowCenter),
                new Point(14, rowCenter));
        }
    }
}
