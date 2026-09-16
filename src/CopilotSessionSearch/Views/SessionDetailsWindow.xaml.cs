#nullable enable

using System.Windows;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch.Views;

public partial class SessionDetailsWindow : Window
{
    public SessionDetailsWindow(SessionDetailsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
