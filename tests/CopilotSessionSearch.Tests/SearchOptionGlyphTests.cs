#nullable enable

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

                        var bitmap = new RenderTargetBitmap(
                            16,
                            16,
                            96,
                            96,
                            PixelFormats.Pbgra32);
                        bitmap.Render(glyph);
                        var pixels = new byte[16 * 16 * 4];
                        bitmap.CopyPixels(
                            pixels,
                            stride: 16 * 4,
                            offset: 0);

                        Assert.Contains(
                            Enumerable.Range(0, 16 * 16),
                            pixelIndex =>
                                pixels[pixelIndex * 4 + 3] > 0);
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
