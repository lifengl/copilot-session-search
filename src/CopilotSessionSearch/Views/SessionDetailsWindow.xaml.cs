#nullable enable

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CopilotSessionSearch.Controls;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch.Views;

public partial class SessionDetailsWindow : Window
{
    public SessionDetailsWindow(SessionDetailsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FocusSelectedMessage();
    }

    private void SessionDetailsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape
            || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
        Close();
    }

    private void MessagesListView_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        ListViewItem? item = ItemsControl.ContainerFromElement(
            MessagesListView,
            source) as ListViewItem;

        if (item is not null)
        {
            MessagesListView.SelectedItem = item.DataContext;
        }
    }

    private void MessagesListView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var viewModel = (SessionDetailsViewModel)DataContext;

        if (e.Key == Key.C
            && Keyboard.Modifiers == ModifierKeys.Control
            && !IsFocusInsideMarkdownViewer())
        {
            if (viewModel.CopySelectedMessageCommand.CanExecute(null))
            {
                viewModel.CopySelectedMessageCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Enter
            && Keyboard.Modifiers == ModifierKeys.None
            && MessagesListView.SelectedItem is not null)
        {
            ListViewItem? item = MessagesListView.ItemContainerGenerator.ContainerFromItem(
                MessagesListView.SelectedItem) as ListViewItem;
            SelectableMarkdownViewer? viewer = item is null
                ? null
                : FindVisualDescendant<SelectableMarkdownViewer>(item);

            if (viewer is not null)
            {
                viewer.Focus();
                e.Handled = true;
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void FocusSelectedMessage()
    {
        if (MessagesListView.SelectedItem is null)
        {
            MessagesListView.SelectedIndex = 0;
        }

        if (MessagesListView.SelectedItem is null)
        {
            MessagesListView.Focus();
            return;
        }

        MessagesListView.ScrollIntoView(MessagesListView.SelectedItem);
        MessagesListView.UpdateLayout();

        ListViewItem? item = MessagesListView.ItemContainerGenerator.ContainerFromItem(
            MessagesListView.SelectedItem) as ListViewItem;
        (item as IInputElement ?? MessagesListView).Focus();
    }

    private static bool IsFocusInsideMarkdownViewer()
    {
        DependencyObject? current = Keyboard.FocusedElement as DependencyObject;
        while (current is not null)
        {
            if (current is SelectableMarkdownViewer)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            T? descendant = FindVisualDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
