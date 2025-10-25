using System;
using System.Runtime.InteropServices;
using System.Windows;
using CmdDashboard.ViewModels;

namespace CmdDashboard.Controls;

public partial class TerminalTile : System.Windows.Controls.UserControl
{
    public TerminalTile()
    {
        InitializeComponent();
    }

    private void InputBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not TerminalSessionViewModel vm)
        {
            return;
        }

        if (!vm.IsInteractive)
        {
            e.Handled = true;
            return;
        }

        if (sender is not System.Windows.Controls.TextBox textBox)
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.Up)
        {
            if (vm.RecallPrevious())
            {
                e.Handled = true;

                textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();

                var caretIndex = vm.PendingInput.Length;
                textBox.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Input,
                    new Action(() =>
                    {
                        textBox.CaretIndex = caretIndex;
                        textBox.ScrollToEnd();
                    }));
            }
            return;
        }

        if (e.Key == System.Windows.Input.Key.Down)
        {
            if (vm.RecallNext())
            {
                e.Handled = true;

                textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();

                var caretIndex = vm.PendingInput.Length;
                textBox.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Input,
                    new Action(() =>
                    {
                        textBox.CaretIndex = caretIndex;
                        textBox.ScrollToEnd();
                    }));
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
        if (sender is System.Windows.Controls.TextBox outputBox)
        {
            outputBox.ScrollToEnd();
        }
    }

    private void CopyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalSessionViewModel vm && !string.IsNullOrEmpty(vm.Output))
        {
            try
            {
                System.Windows.Clipboard.SetText(vm.Output);
            }
            catch (COMException)
            {
                System.Windows.MessageBox.Show("Couldn't access the clipboard. Please try again after closing applications that might be locking it.",
                    "Clipboard Busy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
