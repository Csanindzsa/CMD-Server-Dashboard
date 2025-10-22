using System.IO;
using System.Windows;
using CmdDashboard.Models;
using Forms = System.Windows.Forms;

namespace CmdDashboard.Views;

public partial class NewSessionDialog : Window
{
    public NewSessionDialog()
    {
        InitializeComponent();
    }

    public NewSessionOptions? SessionOptions { get; private set; }

    public void Initialize(string suggestedTitle, string? defaultDirectory)
    {
        TitleBox.Text = suggestedTitle;
        DirectoryBox.Text = defaultDirectory ?? string.Empty;
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select the working directory for the new terminal session",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(DirectoryBox.Text) ? DirectoryBox.Text : string.Empty
        };

        var result = dialog.ShowDialog();
        if (result == Forms.DialogResult.OK)
        {
            DirectoryBox.Text = dialog.SelectedPath;
        }
    }

    private void CreateButton_OnClick(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            System.Windows.MessageBox.Show(this, "Please provide a title for the terminal.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            TitleBox.Focus();
            return;
        }

        var directory = DirectoryBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            System.Windows.MessageBox.Show(this, "The specified working directory does not exist.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            DirectoryBox.Focus();
            return;
        }

        var startupCommand = StartupCommandBox.Text.Trim();
        SessionOptions = new NewSessionOptions(title, string.IsNullOrWhiteSpace(directory) ? null : directory, string.IsNullOrWhiteSpace(startupCommand) ? null : startupCommand);
        DialogResult = true;
    }
}
