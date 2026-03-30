using AppTunnel.UI.Services;
using AppTunnel.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WpfApplication = System.Windows.Application;
using WpfExitEventArgs = System.Windows.ExitEventArgs;
using WpfStartupEventArgs = System.Windows.StartupEventArgs;
using WpfWindow = System.Windows.Window;
using WpfWindowState = System.Windows.WindowState;

namespace AppTunnel.UI;

public partial class App : WpfApplication
{
    private readonly IHost _host;
    private TrayIconHost? _trayIconHost;
    private bool _allowWindowClose;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<AppTunnelControlClient>();
                services.AddSingleton<ExecutableIconService>();
                services.AddSingleton<AppRuleDialogService>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(WpfStartupEventArgs e)
    {
        base.OnStartup(e);

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
        mainWindow.DataContext = viewModel;

        mainWindow.Closing += MainWindowOnClosing;
        mainWindow.StateChanged += MainWindowOnStateChanged;

        _trayIconHost = new TrayIconHost(
            showWindow: ShowMainWindow,
            refresh: async () => await viewModel.RefreshAsync(),
            exportLogs: async () => await viewModel.ExportLogsAsync(),
            exitApplication: ShutdownApplication);

        MainWindow = mainWindow;
        mainWindow.Show();
        mainWindow.Activate();

        await viewModel.InitializeAsync();
    }

    protected override async void OnExit(WpfExitEventArgs e)
    {
        _trayIconHost?.Dispose();
        _host.Services.GetRequiredService<MainWindowViewModel>().StopAutoRefresh();

        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            _host.Dispose();
            base.OnExit(e);
        }
    }

    private void MainWindowOnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowWindowClose)
        {
            return;
        }

        e.Cancel = true;
        if (sender is WpfWindow window)
        {
            window.Hide();
            window.ShowInTaskbar = false;
        }
    }

    private void MainWindowOnStateChanged(object? sender, EventArgs e)
    {
        if (sender is not WpfWindow window)
        {
            return;
        }

        if (window.WindowState == WpfWindowState.Minimized)
        {
            window.Hide();
            window.ShowInTaskbar = false;
        }
    }

    private void ShowMainWindow()
    {
        var window = MainWindow;
        if (window is null)
        {
            return;
        }

        window.Show();
        window.WindowState = WpfWindowState.Normal;
        window.Activate();
        window.ShowInTaskbar = true;
        window.Topmost = true;
        window.Topmost = false;
    }

    private void ShutdownApplication()
    {
        _allowWindowClose = true;
        Shutdown();
    }
}
