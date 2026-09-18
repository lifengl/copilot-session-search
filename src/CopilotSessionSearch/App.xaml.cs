using System.Windows;
using System.IO;
using System.Text.Json;
using CopilotSessionSearch.Models;
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
            var themePreferenceStore = new ThemePreferenceStore();
            AppThemePreference themePreference = LoadThemePreference(themePreferenceStore);
            var themeService = new ThemeService();
            themeService.Apply(themePreference);
            var historySource = new CopilotSdkSessionHistorySource();
            var documentCache = new SessionDocumentCache();
            var hybridSearchIndex = new PersistentHybridSearchIndex(
                PersistentHybridSearchIndex.GetDefaultDatabasePath());
            var aiSearchCoordinator = new AiSessionSearchCoordinator(
                historySource,
                documentCache,
                hybridSearchIndex,
                new CopilotAiSearchSessionFactory(AppContext.BaseDirectory));
            var searchCoordinator = new SessionSearchCoordinator(
                historySource,
                documentCache,
                new SessionSearchService(),
                aiSearchCoordinator);
            var viewModel = new MainWindowViewModel(
                searchCoordinator,
                historySource,
                themeService,
                themePreferenceStore,
                hybridSearchIndex);
            var window = new MainWindow(
                viewModel,
                new ConsoleLauncher(),
                new ClipboardService());

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

    private static AppThemePreference LoadThemePreference(
        IThemePreferenceStore themePreferenceStore)
    {
        try
        {
            return themePreferenceStore.Load() ?? AppThemePreference.System;
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or NotSupportedException)
        {
            MessageBox.Show(
                $"The saved theme preference could not be read. System theme will be used.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Copilot Session Search",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return AppThemePreference.System;
        }
    }
}
