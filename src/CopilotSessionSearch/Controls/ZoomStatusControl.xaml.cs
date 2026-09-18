#nullable enable

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch.Controls;

public partial class ZoomStatusControl : UserControl
{
    private long? _previousClickTimestamp;

    public ZoomStatusControl()
    {
        InitializeComponent();
        ZoomPopup.CustomPopupPlacementCallback = PlacePopup;
        DataContextChanged += OnDataContextChanged;
    }

    public static bool IsSourceWithinZoomControl(object? source)
    {
        DependencyObject? current = source as DependencyObject;
        while (current is not null)
        {
            if (current is FrameworkElement element
                && element.DataContext is WindowZoomViewModel)
            {
                return true;
            }

            if (current is FrameworkContentElement contentElement
                && contentElement.DataContext is WindowZoomViewModel)
            {
                return true;
            }

            current = current switch
            {
                Visual or Visual3D =>
                    VisualTreeHelper.GetParent(current),
                FrameworkContentElement frameworkContentElement =>
                    frameworkContentElement.Parent,
                _ => LogicalTreeHelper.GetParent(current),
            };
        }

        return false;
    }

    private void ZoomButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        WindowZoomViewModel zoom = GetZoom();
        long currentTimestamp = Environment.TickCount64;
        if (_previousClickTimestamp is long previousTimestamp
            && currentTimestamp - previousTimestamp
                <= NativeMethods.GetDoubleClickTime())
        {
            _previousClickTimestamp = null;
            zoom.ResetAndClosePopupCommand.Execute(null);
            return;
        }

        _previousClickTimestamp = currentTimestamp;
        zoom.OpenPopupCommand.Execute(null);
    }

    private void ZoomButton_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        _previousClickTimestamp = null;
        GetZoom().ResetAndClosePopupCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is WindowZoomViewModel previousZoom)
        {
            previousZoom.PropertyChanged -= OnZoomPropertyChanged;
        }

        if (e.NewValue is WindowZoomViewModel zoom)
        {
            zoom.PropertyChanged += OnZoomPropertyChanged;
        }
    }

    private void OnZoomPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WindowZoomViewModel.IsPopupOpen)
            && sender is WindowZoomViewModel zoom
            && !zoom.IsPopupOpen)
        {
            _previousClickTimestamp = null;
        }
    }

    private WindowZoomViewModel GetZoom()
    {
        return (WindowZoomViewModel)DataContext;
    }

    private static CustomPopupPlacement[] PlacePopup(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        const double Gap = 4;
        return
        [
            new CustomPopupPlacement(
                new Point(
                    (targetSize.Width - popupSize.Width) / 2,
                    -popupSize.Height - Gap),
                PopupPrimaryAxis.Horizontal),
        ];
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern uint GetDoubleClickTime();
    }
}
