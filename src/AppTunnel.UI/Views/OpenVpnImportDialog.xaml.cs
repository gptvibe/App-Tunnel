using System.IO;
using System.Windows;
using AppTunnel.UI.Services;
using WpfMessageBox = System.Windows.MessageBox;
using WpfWindow = System.Windows.Window;

namespace AppTunnel.UI.Views;

public partial class OpenVpnImportDialog : WpfWindow
{
    public OpenVpnImportDialog(string sourcePath)
    {
        SourcePath = Path.GetFullPath(sourcePath);
        SuggestedDisplayName = Path.GetFileNameWithoutExtension(SourcePath);
        InitializeComponent();
        DataContext = this;
        DisplayNameTextBox.Text = SuggestedDisplayName;
    }

    public string SourcePath { get; }

    public string SuggestedDisplayName { get; }

    public OpenVpnImportDialogResult? Result { get; private set; }

    private void ImportButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameTextBox.Text))
        {
            WpfMessageBox.Show(this, "Enter a display name before importing the OpenVPN profile.", "App Tunnel", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new OpenVpnImportDialogResult(
            DisplayNameTextBox.Text.Trim(),
            string.IsNullOrWhiteSpace(UsernameTextBox.Text) ? null : UsernameTextBox.Text.Trim(),
            string.IsNullOrWhiteSpace(PasswordTextBox.Password) ? null : PasswordTextBox.Password);
        DialogResult = true;
    }

    private void CancelButtonOnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
