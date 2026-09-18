#nullable enable

using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CopilotSessionSearch.Controls;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch.Tests;

public sealed class ZoomStatusControlTests
{
    [Fact]
    public void ConsecutiveCompletedClicksResetZoomAndClosePopup()
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    using var zoom = new WindowZoomViewModel(150);
                    var control = new ZoomStatusControl
                    {
                        DataContext = zoom,
                    };
                    var button = (Button)control.FindName("ZoomButton");

                    button.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));

                    Assert.True(zoom.IsPopupOpen);
                    Assert.Equal(150, zoom.Percentage);

                    button.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));

                    Assert.False(zoom.IsPopupOpen);
                    Assert.Equal(100, zoom.Percentage);
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

    [Fact]
    public void PopupPlacementCentersFlyoutAboveButton()
    {
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    var control = new ZoomStatusControl();
                    var popup = (Popup)control.FindName("ZoomPopup");
                    var popupSize = new Size(94, 28);
                    var buttonSize = new Size(48, 24);

                    var placement = Assert.Single(
                        popup.CustomPopupPlacementCallback(
                            popupSize,
                            buttonSize,
                            default));

                    Assert.Equal(
                        buttonSize.Width / 2,
                        placement.Point.X + popupSize.Width / 2);
                    Assert.Equal(-32, placement.Point.Y);
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
