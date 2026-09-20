#nullable enable

using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using CopilotSessionSearch.Controls;

namespace CopilotSessionSearch.Tests;

public sealed class SearchOptionGlyphTests
{
    [Fact]
    public void EverySearchOptionKindRendersVisiblePixels()
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    foreach (SearchOptionGlyphKind kind in
                        Enum.GetValues<SearchOptionGlyphKind>())
                    {
                        var glyph = new SearchOptionGlyph
                        {
                            Foreground = Brushes.White,
                            Height = 16,
                            Kind = kind,
                            Width = 16,
                        };
                        glyph.Measure(new Size(16, 16));
                        glyph.Arrange(new Rect(0, 0, 16, 16));
                        MethodInfo onRender =
                            typeof(SearchOptionGlyph).GetMethod(
                                "OnRender",
                                BindingFlags.Instance
                                    | BindingFlags.NonPublic)
                            ?? throw new MissingMethodException(
                                typeof(SearchOptionGlyph).FullName,
                                "OnRender");
                        var visual = new DrawingVisual();
                        using (DrawingContext drawingContext =
                            visual.RenderOpen())
                        {
                            onRender.Invoke(
                                glyph,
                                [drawingContext]);
                        }

                        Assert.NotNull(visual.Drawing);
                        Assert.NotEmpty(visual.Drawing.Children);
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
