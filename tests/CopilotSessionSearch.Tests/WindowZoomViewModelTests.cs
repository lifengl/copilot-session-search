#nullable enable

using System.Windows.Input;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch.Tests;

public sealed class WindowZoomViewModelTests
{
    [Fact]
    public void ShortcutsUseTenPercentStepsAndRespectBounds()
    {
        var zoom = new WindowZoomViewModel();

        Assert.Equal(100, zoom.Percentage);
        Assert.Equal(1d, zoom.Scale);
        Assert.Equal("100%", zoom.PercentageText);
        Assert.Equal("Zoom 100%", zoom.AccessibleText);

        Assert.True(
            zoom.TryHandleShortcut(
                Key.OemPlus,
                ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal(110, zoom.Percentage);
        Assert.True(
            zoom.TryHandleShortcut(
                Key.OemMinus,
                ModifierKeys.Control));
        Assert.Equal(100, zoom.Percentage);

        for (int index = 0; index < 20; index++)
        {
            zoom.ZoomInCommand.Execute(null);
        }

        Assert.Equal(200, zoom.Percentage);
        Assert.False(zoom.ZoomInCommand.CanExecute(null));

        for (int index = 0; index < 20; index++)
        {
            zoom.ZoomOutCommand.Execute(null);
        }

        Assert.Equal(50, zoom.Percentage);
        Assert.False(zoom.ZoomOutCommand.CanExecute(null));

        Assert.True(
            zoom.TryHandleShortcut(
                Key.NumPad0,
                ModifierKeys.Control));
        Assert.Equal(100, zoom.Percentage);
        Assert.False(zoom.ResetZoomCommand.CanExecute(null));
    }

    [Fact]
    public void SliderValueSnapsToSupportedZoomStep()
    {
        var zoom = new WindowZoomViewModel();

        zoom.Percentage = 137;

        Assert.Equal(140, zoom.Percentage);
        Assert.Equal(1.4d, zoom.Scale);
        Assert.Equal("Zoom 140%", zoom.AccessibleText);
    }

    [Fact]
    public void PopupCommandsOpenCloseAndResetLocalState()
    {
        using var zoom = new WindowZoomViewModel(140);

        zoom.OpenPopupCommand.Execute(null);

        Assert.True(zoom.IsPopupOpen);

        zoom.ResetAndClosePopupCommand.Execute(null);

        Assert.False(zoom.IsPopupOpen);
        Assert.Equal(100, zoom.Percentage);
    }

    [Fact]
    public async Task PopupClosesAfterConfiguredIdleTime()
    {
        using var zoom = new WindowZoomViewModel(
            popupIdleTimeout: TimeSpan.FromMilliseconds(50));

        zoom.OpenPopupCommand.Execute(null);

        Assert.True(zoom.IsPopupOpen);

        await WaitUntilAsync(
            () => !zoom.IsPopupOpen,
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ZoomChangeRestartsPopupIdleTimer()
    {
        using var zoom = new WindowZoomViewModel(
            popupIdleTimeout: TimeSpan.FromMilliseconds(150));
        zoom.OpenPopupCommand.Execute(null);
        await Task.Delay(100);

        zoom.Percentage = 120;
        await Task.Delay(100);

        Assert.True(zoom.IsPopupOpen);

        await WaitUntilAsync(
            () => !zoom.IsPopupOpen,
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void CopiedZoomStateChangesIndependently()
    {
        var mainZoom = new WindowZoomViewModel(130);
        var detailZoom = new WindowZoomViewModel(
            mainZoom.Percentage);

        mainZoom.ZoomInCommand.Execute(null);
        detailZoom.ZoomOutCommand.Execute(null);

        Assert.Equal(140, mainZoom.Percentage);
        Assert.Equal(120, detailZoom.Percentage);
    }

    [Fact]
    public void UnsupportedShortcutDoesNotChangeZoom()
    {
        var zoom = new WindowZoomViewModel();

        Assert.False(
            zoom.TryHandleShortcut(
                Key.OemPlus,
                ModifierKeys.Alt));
        Assert.False(
            zoom.TryHandleShortcut(
                Key.A,
                ModifierKeys.Control));
        Assert.Equal(100, zoom.Percentage);
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    "The expected zoom state was not reached.");
            }

            await Task.Delay(10);
        }
    }
}
