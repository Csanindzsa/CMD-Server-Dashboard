using System.IO;
using System.Windows;
using CmdDashboard.Models;
using CmdDashboard;
using Forms = System.Windows.Forms;

namespace CmdDashboard.Views;

public partial class NewSessionDialog : Window
{
    public NewSessionDialog()
    {
        InitializeComponent();
    }

    public NewSessionOptions? SessionOptions { get; private set; }

    public void Initialize(string suggestedTitle, string? defaultDirectory, bool isAppElevated)
    {
        TitleBox.Text = suggestedTitle;
        DirectoryBox.Text = defaultDirectory ?? string.Empty;
        TitleBox.Focus();
        TitleBox.SelectAll();

        if (isAppElevated)
        {
            AdminCheckBox.IsChecked = true;
            AdminCheckBox.IsEnabled = false;
            AdminCheckBox.Content = "Run terminal as administrator (application is elevated)";
        }
        else
        {
            AdminCheckBox.IsChecked = false;
            AdminCheckBox.IsEnabled = true;
        }
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
        var runAsAdmin = AdminCheckBox.IsChecked == true;

        if (runAsAdmin && !App.IsRunningAsAdministrator)
        {
            System.Windows.MessageBox.Show(this,
                "Elevated terminals require the Command Dashboard application to run as administrator.",
                "Administrator Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SessionOptions = new NewSessionOptions(
            title,
            string.IsNullOrWhiteSpace(directory) ? null : directory,
            string.IsNullOrWhiteSpace(startupCommand) ? null : startupCommand,
            runAsAdmin || App.IsRunningAsAdministrator);
        DialogResult = true;
    }
}
