using System.IO;
using System.Windows;
using AppTunnel.UI.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfMessageBox = System.Windows.MessageBox;
using WpfWindow = System.Windows.Window;

namespace AppTunnel.UI.Views;

public partial class AddAppDialog : WpfWindow
{
    private readonly ExecutableIconService _executableIconService;

    public AddAppDialog(ExecutableIconService executableIconService)
    {
        _executableIconService = executableIconService;
        InitializeComponent();
    }

    public AddWin32AppDialogResult? Result { get; private set; }

    private void BrowseButtonOnClick(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Executable (*.exe)|*.exe",
            Title = "Select Win32 app",
        };

        if (openFileDialog.ShowDialog(this) != true)
        {
            return;
        }

        ExecutablePathTextBox.Text = openFileDialog.FileName;
        if (string.IsNullOrWhiteSpace(DisplayNameTextBox.Text))
        {
            DisplayNameTextBox.Text = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
        }

        UpdatePreview(openFileDialog.FileName);
    }

    private void ExecutablePathTextBoxOnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdatePreview(ExecutablePathTextBox.Text);
    }

    private void AddButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ExecutablePathTextBox.Text))
        {
            WpfMessageBox.Show(this, "Select an .exe before adding the rule.", "App Tunnel", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new AddWin32AppDialogResult(
            ExecutablePathTextBox.Text.Trim(),
            string.IsNullOrWhiteSpace(DisplayNameTextBox.Text) ? null : DisplayNameTextBox.Text.Trim());
        DialogResult = true;
    }

    private void CancelButtonOnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdatePreview(string? executablePath)
    {
        var iconSource = _executableIconService.GetIcon(executablePath);
        IconPreview.Source = iconSource;
        FallbackMonogram.Visibility = iconSource is null ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var fileName = Path.GetFileNameWithoutExtension(executablePath);
            FallbackMonogram.Text = string.IsNullOrWhiteSpace(fileName)
                ? "A"
                : char.ToUpperInvariant(fileName[0]).ToString();
        }
    }
}