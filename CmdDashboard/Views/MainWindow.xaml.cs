using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CmdDashboard.ViewModels;

namespace CmdDashboard.Views;

public partial class MainWindow : Window
{
    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void NewTerminalButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new NewSessionDialog
        {
            Owner = this
        };

        var suggestedTitle = $"Terminal {viewModel.Sessions.Count + 1}";
        string? defaultDirectory = TryResolveStartupDirectory();
        dialog.Initialize(suggestedTitle, defaultDirectory);

        var result = dialog.ShowDialog();
        if (result == true && dialog.SessionOptions is { } options)
        {
            viewModel.AddSession(options);
        }
    }

    private static string? TryResolveStartupDirectory()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void NotesTiles_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateVisibleNoteCapacity(e.NewSize.Width);
        }
    }

    private void NoteTile_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { DataContext: NoteViewModel note })
        {
            viewModel.SelectedNote = note;
        }
    }

    private void NoteTile_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { DataContext: NoteViewModel note })
        {
            viewModel.SelectedNote = note;
        }
    }

    private void NoteTile_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: NoteViewModel note } element && element.ContextMenu is { } menu)
        {
            viewModel.SelectedNote = note;
            menu.DataContext = viewModel;
        }
    }

    private void NoteTileTextBox_OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { DataContext: NoteViewModel note })
        {
            viewModel.SelectedNote = note;
        }
    }

    private void TerminalList_OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    private void TerminalList_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
    if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        var currentPos = e.GetPosition(null);
        if (_isDragging || (Math.Abs(currentPos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                            Math.Abs(currentPos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance))
        {
            return;
        }

        if (sender is not System.Windows.Controls.ListBox)
        {
            return;
        }

        var item = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not TerminalSessionViewModel session)
        {
            return;
        }

    _isDragging = true;
    System.Windows.DragDrop.DoDragDrop(item, session, System.Windows.DragDropEffects.Move);
    }

    private void TerminalList_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        _isDragging = false;

    if (DataContext is not MainViewModel viewModel || sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        if (!e.Data.GetDataPresent(typeof(TerminalSessionViewModel)))
        {
            return;
        }

        var droppedSession = (TerminalSessionViewModel)e.Data.GetData(typeof(TerminalSessionViewModel))!;
        var oldIndex = viewModel.Sessions.IndexOf(droppedSession);
        if (oldIndex < 0)
        {
            return;
        }

        var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        var newIndex = targetItem != null
            ? listBox.ItemContainerGenerator.IndexFromContainer(targetItem)
            : viewModel.Sessions.Count - 1;

        if (newIndex < 0)
        {
            newIndex = 0;
        }

        if (newIndex >= viewModel.Sessions.Count)
        {
            newIndex = viewModel.Sessions.Count - 1;
        }

        if (newIndex == oldIndex)
        {
            return;
        }

        viewModel.Sessions.Move(oldIndex, newIndex);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
