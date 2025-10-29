using System.Runtime.InteropServices;
using System;
using System.Windows;
using CmdDashboard.ViewModels;
using WpfDispatcherPriority = System.Windows.Threading.DispatcherPriority;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfTextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;

namespace CmdDashboard.Controls;

public partial class TerminalTile : System.Windows.Controls.UserControl
{
    public TerminalTile()
    {
        var resourceLocator = new Uri("/CmdDashboard;component/Controls/TerminalTile.xaml", UriKind.Relative);
        System.Windows.Application.LoadComponent(this, resourceLocator);
    }

    private void InputBox_OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
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

        if (sender is not WpfTextBox textBox)
        {
            return;
        }

        if (e.Key == WpfKey.C && (WpfKeyboard.Modifiers & WpfModifierKeys.Control) == WpfModifierKeys.Control)
        {
            if (textBox.SelectionLength > 0)
            {
                return;
            }

            if (vm.TryInterrupt())
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key == WpfKey.Tab)
        {
            var reverse = (WpfKeyboard.Modifiers & WpfModifierKeys.Shift) == WpfModifierKeys.Shift;
            if (vm.TryAutoComplete(reverse, textBox.Text, textBox.CaretIndex, out var newCaretIndex))
            {
                textBox.Dispatcher.BeginInvoke(
                    WpfDispatcherPriority.Input,
                    new Action(() =>
                    {
                        textBox.CaretIndex = newCaretIndex;
                        var lineIndex = textBox.GetLineIndexFromCharacterIndex(Math.Min(newCaretIndex, textBox.Text.Length));
                        if (lineIndex >= 0)
                        {
                            textBox.ScrollToLine(lineIndex);
                        }
                    }));
            }

            e.Handled = true;
            return;
        }

        if (e.Key == WpfKey.Up)
        {
            if (vm.RecallPrevious())
            {
                e.Handled = true;

                textBox.GetBindingExpression(WpfTextBox.TextProperty)?.UpdateTarget();

                var caretIndex = vm.PendingInput.Length;
                textBox.Dispatcher.BeginInvoke(
                    WpfDispatcherPriority.Input,
                    new Action(() =>
                    {
                        textBox.CaretIndex = caretIndex;
                        textBox.ScrollToEnd();
                    }));
            }
            return;
        }

        if (e.Key == WpfKey.Down)
        {
            if (vm.RecallNext())
            {
                e.Handled = true;

                textBox.GetBindingExpression(WpfTextBox.TextProperty)?.UpdateTarget();

                var caretIndex = vm.PendingInput.Length;
                textBox.Dispatcher.BeginInvoke(
                    WpfDispatcherPriority.Input,
                    new Action(() =>
                    {
                        textBox.CaretIndex = caretIndex;
                        textBox.ScrollToEnd();
                    }));
            }
            return;
        }

        if (e.Key == WpfKey.Enter)
        {
            if ((WpfKeyboard.Modifiers & WpfModifierKeys.Shift) == WpfModifierKeys.Shift)
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

        if (e.Key != WpfKey.Enter)
        {
            return;
        }
    }

    private void OutputBox_OnTextChanged(object sender, WpfTextChangedEventArgs e)
    {
        if (sender is WpfTextBox outputBox)
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
