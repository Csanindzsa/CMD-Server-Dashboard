using System.Windows;
using CmdDashboard.ViewModels;

namespace CmdDashboard.Controls;

public partial class TerminalTile : System.Windows.Controls.UserControl
{
    public TerminalTile()
    {
        InitializeComponent();
    }

    private void InputBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
    if (e.Key != System.Windows.Input.Key.Enter)
        {
            return;
        }

        if (DataContext is TerminalSessionViewModel vm && vm.SendInputCommand.CanExecute(null))
        {
            vm.SendInputCommand.Execute(null);
            e.Handled = true;
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                textBox.CaretIndex = textBox.Text.Length;
            }
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
}
