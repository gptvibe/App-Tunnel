using System.Windows;
using AppTunnel.UI.Services;
using AppTunnel.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AppTunnel.UI;

public partial class App : Application
{
	private readonly IHost _host;

	public App()
	{
		_host = Host.CreateDefaultBuilder()
			.ConfigureServices(services =>
			{
				services.AddSingleton<AppTunnelControlClient>();
				services.AddSingleton<MainWindowViewModel>();
				services.AddSingleton<MainWindow>(serviceProvider =>
				{
					var window = new MainWindow
					{
						DataContext = serviceProvider.GetRequiredService<MainWindowViewModel>(),
					};

					return window;
				});
			})
			.Build();
	}

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		await _host.StartAsync();

		var mainWindow = _host.Services.GetRequiredService<MainWindow>();
		var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();

		MainWindow = mainWindow;
		mainWindow.Show();
		await viewModel.InitializeAsync();
	}

	protected override async void OnExit(ExitEventArgs e)
	{
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
}

