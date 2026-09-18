#nullable enable

using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CopilotSessionSearch.ViewModels;

public sealed partial class WindowZoomViewModel : ObservableObject, IDisposable
{
    public const int DefaultPercentage = 100;
    public const int MinimumPercentage = 50;
    public const int MaximumPercentage = 200;
    public const int StepPercentage = 10;

    private static readonly TimeSpan DefaultPopupIdleTimeout =
        TimeSpan.FromSeconds(5);

    public static double MinimumSliderPercentage => MinimumPercentage;

    public static double MaximumSliderPercentage => MaximumPercentage;

    public static double SliderStepPercentage => StepPercentage;

    private readonly TimeSpan _popupIdleTimeout;
    private readonly Dispatcher? _dispatcher;
    private int _percentage;
    private CancellationTokenSource? _popupIdleCancellationSource;
    private bool _disposed;

    [ObservableProperty]
    private bool _isPopupOpen;

    public WindowZoomViewModel(
        int initialPercentage = DefaultPercentage,
        TimeSpan? popupIdleTimeout = null)
    {
        if (initialPercentage < MinimumPercentage
            || initialPercentage > MaximumPercentage
            || initialPercentage % StepPercentage != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialPercentage),
                $"Zoom must be between {MinimumPercentage}% and " +
                $"{MaximumPercentage}% in {StepPercentage}% steps.");
        }

        TimeSpan resolvedPopupIdleTimeout =
            popupIdleTimeout ?? DefaultPopupIdleTimeout;
        if (resolvedPopupIdleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(popupIdleTimeout),
                "The popup idle timeout must be greater than zero.");
        }

        _percentage = initialPercentage;
        _popupIdleTimeout = resolvedPopupIdleTimeout;
        _dispatcher = System.Windows.Application.Current?.Dispatcher;
    }

    public int Percentage
    {
        get => _percentage;
        set
        {
            int normalizedPercentage = Math.Clamp(
                (int)Math.Round(
                    value / (double)StepPercentage,
                    MidpointRounding.AwayFromZero) * StepPercentage,
                MinimumPercentage,
                MaximumPercentage);
            if (!SetProperty(
                ref _percentage,
                normalizedPercentage))
            {
                return;
            }

            OnPropertyChanged(nameof(Scale));
            OnPropertyChanged(nameof(PercentageText));
            OnPropertyChanged(nameof(AccessibleText));
            ZoomInCommand.NotifyCanExecuteChanged();
            ZoomOutCommand.NotifyCanExecuteChanged();
            ResetZoomCommand.NotifyCanExecuteChanged();

            if (IsPopupOpen)
            {
                RestartPopupIdleTimer();
            }
        }
    }

    public double Scale => Percentage / 100d;

    public string PercentageText => $"{Percentage}%";

    public string AccessibleText => $"Zoom {Percentage}%";

    public bool TryHandleShortcut(
        Key key,
        ModifierKeys modifiers)
    {
        if (key == Key.OemPlus
            && modifiers is ModifierKeys.Control
                or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ZoomIn();
            return true;
        }

        if (modifiers != ModifierKeys.Control)
        {
            return false;
        }

        switch (key)
        {
            case Key.Add:
                ZoomIn();
                return true;

            case Key.OemMinus:
            case Key.Subtract:
                ZoomOut();
                return true;

            case Key.D0:
            case Key.NumPad0:
                ResetZoom();
                return true;

            default:
                return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPopupIdleTimer();
        IsPopupOpen = false;
    }

    private bool CanZoomIn()
    {
        return Percentage < MaximumPercentage;
    }

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn()
    {
        Percentage = Math.Min(
            MaximumPercentage,
            Percentage + StepPercentage);
    }

    private bool CanZoomOut()
    {
        return Percentage > MinimumPercentage;
    }

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut()
    {
        Percentage = Math.Max(
            MinimumPercentage,
            Percentage - StepPercentage);
    }

    private bool CanResetZoom()
    {
        return Percentage != DefaultPercentage;
    }

    [RelayCommand(CanExecute = nameof(CanResetZoom))]
    private void ResetZoom()
    {
        Percentage = DefaultPercentage;
    }

    [RelayCommand]
    private void OpenPopup()
    {
        if (IsPopupOpen)
        {
            RestartPopupIdleTimer();
        }
        else
        {
            IsPopupOpen = true;
        }
    }

    [RelayCommand]
    private void ClosePopup()
    {
        IsPopupOpen = false;
    }

    [RelayCommand]
    private void ResetAndClosePopup()
    {
        IsPopupOpen = false;
        ResetZoom();
    }

    partial void OnIsPopupOpenChanged(bool value)
    {
        if (value)
        {
            RestartPopupIdleTimer();
        }
        else
        {
            CancelPopupIdleTimer();
        }
    }

    private void RestartPopupIdleTimer()
    {
        if (_disposed)
        {
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        CancellationTokenSource? previousSource = Interlocked.Exchange(
            ref _popupIdleCancellationSource,
            cancellationSource);
        CancelAndDispose(previousSource);
        _ = ClosePopupAfterIdleAsync(cancellationSource);
    }

    private async Task ClosePopupAfterIdleAsync(
        CancellationTokenSource cancellationSource)
    {
        try
        {
            await Task.Delay(
                _popupIdleTimeout,
                cancellationSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationSource.IsCancellationRequested)
        {
            return;
        }

        void CloseIfCurrent()
        {
            if (!ReferenceEquals(
                Interlocked.CompareExchange(
                    ref _popupIdleCancellationSource,
                    null,
                    cancellationSource),
                cancellationSource))
            {
                return;
            }

            cancellationSource.Dispose();
            IsPopupOpen = false;
        }

        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            CloseIfCurrent();
        }
        else if (!_dispatcher.HasShutdownStarted)
        {
            _ = _dispatcher.BeginInvoke(CloseIfCurrent);
        }
        else
        {
            CancelPopupIdleTimer();
        }
    }

    private void CancelPopupIdleTimer()
    {
        CancellationTokenSource? cancellationSource = Interlocked.Exchange(
            ref _popupIdleCancellationSource,
            null);
        CancelAndDispose(cancellationSource);
    }

    private static void CancelAndDispose(
        CancellationTokenSource? cancellationSource)
    {
        if (cancellationSource is null)
        {
            return;
        }

        cancellationSource.Cancel();
        cancellationSource.Dispose();
    }
}
