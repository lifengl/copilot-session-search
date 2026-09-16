using System.ComponentModel;
using System.Windows;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;
using CopilotSessionSearch.ViewModels;
using CopilotSessionSearch.Views;

namespace CopilotSessionSearch;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IConsoleLauncher _consoleLauncher;
    private readonly IClipboardService _clipboardService;
    private readonly Dictionary<string, SessionDetailsWindow> _detailWindows =
        new(StringComparer.Ordinal);
    private bool _allowClose;

    public MainWindow(
        MainWindowViewModel viewModel,
        IConsoleLauncher consoleLauncher,
        IClipboardService clipboardService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(consoleLauncher);
        ArgumentNullException.ThrowIfNull(clipboardService);

        InitializeComponent();

        _viewModel = viewModel;
        _consoleLauncher = consoleLauncher;
        _clipboardService = clipboardService;
        DataContext = viewModel;

        _viewModel.OpenDetailsRequested += ShowDetails;
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Focus();
    }

    private void ShowDetails(SessionSearchResult result)
    {
        string windowKey = result.Session.SessionId + "\0" + result.Query;
        if (_detailWindows.TryGetValue(windowKey, out SessionDetailsWindow? existingWindow))
        {
            existingWindow.Activate();
            return;
        }

        var detailsWindow = new SessionDetailsWindow(
            new SessionDetailsViewModel(
                result,
                _consoleLauncher,
                _clipboardService))
        {
            Owner = this,
        };

        detailsWindow.Closed += (_, _) => _detailWindows.Remove(windowKey);
        _detailWindows.Add(windowKey, detailsWindow);
        detailsWindow.Show();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        IsEnabled = false;

        try
        {
            _viewModel.OpenDetailsRequested -= ShowDetails;
            await _viewModel.DisposeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"The Copilot history service did not shut down cleanly.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Copilot Session Search",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _allowClose = true;
            Close();
            Application.Current.Shutdown();
        }
    }
}