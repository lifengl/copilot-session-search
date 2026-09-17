#nullable enable

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using CopilotSessionSearch.Controls;
using CopilotSessionSearch.ViewModels;

namespace CopilotSessionSearch.Views;

public partial class SessionDetailsWindow : Window
{
    private readonly SessionDetailsViewModel _viewModel;

    public SessionDetailsWindow(SessionDetailsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.MessageViewChanged += OnMessageViewChanged;
        Closed += OnClosed;
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
        if (e.Key == Key.C
            && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SelectableMarkdownViewer? focusedViewer = GetFocusedMarkdownViewer();
            CopyRenderedContent(
                focusedViewer ?? GetSelectedMarkdownViewer(),
                preferSelection: focusedViewer is not null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter
            && Keyboard.Modifiers == ModifierKeys.None
            && MessagesListView.SelectedItem is not null)
        {
            SelectableMarkdownViewer? viewer = GetSelectedMarkdownViewer();

            if (viewer is not null)
            {
                viewer.Focus();
                e.Handled = true;
            }
        }
    }

    private void CopyWholeMessageButton_Click(object sender, RoutedEventArgs e)
    {
        CopyRenderedContent(
            GetSelectedMarkdownViewer(),
            preferSelection: false);
    }

    private void MessageViewMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageViewMenuButton.ContextMenu is not ContextMenu contextMenu)
        {
            return;
        }

        contextMenu.PlacementTarget = MessageViewMenuButton;
        contextMenu.IsOpen = true;
    }

    private void OnMessageViewChanged()
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(BringSelectedMessageIntoView));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.MessageViewChanged -= OnMessageViewChanged;
    }

    private void BringSelectedMessageIntoView()
    {
        if (_viewModel.SelectedMessage is null)
        {
            return;
        }

        MessagesListView.ScrollIntoView(_viewModel.SelectedMessage);
        MessagesListView.UpdateLayout();
    }

    private void CopyRenderedContent(
        SelectableMarkdownViewer? viewer,
        bool preferSelection)
    {
        try
        {
            bool copiedSelection =
                preferSelection && viewer?.CopySelection() is true;
            bool copied =
                copiedSelection || viewer?.CopyWholeMessage() is true;

            if (!copied)
            {
                _viewModel.ErrorMessage =
                    "Unable to copy the message because its rendered content is unavailable.";
                _viewModel.StatusMessage = null;
                return;
            }

            _viewModel.ErrorMessage = null;
            _viewModel.StatusMessage = copiedSelection
                ? "The selected text was copied with rich formatting."
                : "The whole message was copied with rich formatting.";
        }
        catch (Exception ex) when (
            ex is ExternalException
            or InvalidDataException
            or InvalidOperationException
            or XmlException)
        {
            _viewModel.ErrorMessage = $"Unable to copy the message: {ex.Message}";
            _viewModel.StatusMessage = null;
        }
    }

    private SelectableMarkdownViewer? GetSelectedMarkdownViewer()
    {
        if (MessagesListView.SelectedItem is null)
        {
            return null;
        }

        ListViewItem? item = MessagesListView.ItemContainerGenerator.ContainerFromItem(
            MessagesListView.SelectedItem) as ListViewItem;
        if (item is null)
        {
            MessagesListView.ScrollIntoView(MessagesListView.SelectedItem);
            MessagesListView.UpdateLayout();
            item = MessagesListView.ItemContainerGenerator.ContainerFromItem(
                MessagesListView.SelectedItem) as ListViewItem;
        }

        return item is null
            ? null
            : FindVisualDescendant<SelectableMarkdownViewer>(item);
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

    private static SelectableMarkdownViewer? GetFocusedMarkdownViewer()
    {
        DependencyObject? current = Keyboard.FocusedElement as DependencyObject;
        while (current is not null)
        {
            if (current is SelectableMarkdownViewer viewer)
            {
                return viewer;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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
