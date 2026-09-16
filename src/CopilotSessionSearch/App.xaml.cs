using System.Windows;
using CopilotSessionSearch.Services;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var historySource = new CopilotSdkSessionHistorySource();
            var searchCoordinator = new SessionSearchCoordinator(
                historySource,
                new SessionDocumentCache(),
                new SessionSearchService());
            var viewModel = new MainWindowViewModel(searchCoordinator, historySource);
            var window = new MainWindow(viewModel, new ConsoleLauncher());

            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Copilot Session Search could not start.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Copilot Session Search",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
