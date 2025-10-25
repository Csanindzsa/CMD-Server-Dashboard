using System.Windows;
using CmdDashboard.ViewModels;
using CmdDashboard.Views;

namespace CmdDashboard.Controls;

public partial class TerminalTile : System.Windows.Controls.UserControl
{
    public TerminalTile()
    {
        InitializeComponent();
    }

    private void InputBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not TerminalSessionViewModel vm)
        {
            return;
        }

        if (sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.Up)
        {
            if (vm.TryRecallPrevious(out var previous))
            {
                textBox.Text = previous;
                textBox.CaretIndex = textBox.Text.Length;
                textBox.ScrollToEnd();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == System.Windows.Input.Key.Down)
        {
            if (vm.TryRecallNext(out var next))
            {
                textBox.Text = next;
                textBox.CaretIndex = textBox.Text.Length;
                textBox.ScrollToEnd();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift)
            {
                return;
            }

            if (vm.SendInputCommand.CanExecute(null))
            {
                vm.SendInputCommand.Execute(null);
                e.Handled = true;
                textBox.CaretIndex = textBox.Text.Length;
                textBox.ScrollToEnd();
            }

            return;
        }

        if (e.Key != System.Windows.Input.Key.Enter)
        {
            return;
        }
    }

    private void OutputBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ScrollViewer.ScrollToEnd();
    }

    private void CopyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalSessionViewModel vm && !string.IsNullOrEmpty(vm.Output))
        {
            System.Windows.Clipboard.SetText(vm.Output);
        }
    }

    private void OutputBox_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
        {
            mainWindow.RequestClearAllUserData();
            e.Handled = true;
        }
    }
}
