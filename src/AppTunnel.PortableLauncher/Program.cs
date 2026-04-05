using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AppTunnel.Deployment;

try
{
    var rootDirectory = PortableLayout.GetRootDirectory();
    PortableLayout.EnsureLayout(rootDirectory);
    var deploymentPlan = PortableDeploymentPlanFactory.Create(rootDirectory);

    var serviceRegistration = new WindowsServiceRegistration(
        deploymentPlan.ServiceOptions);

    if (args.Contains("--register", StringComparer.OrdinalIgnoreCase))
    {
        serviceRegistration.EnsureRegistered();
        serviceRegistration.EnsureStarted();
        return;
    }

    if (!serviceRegistration.Exists() || !serviceRegistration.IsRunning())
    {
        if (!WindowsServiceRegistration.IsAdministrator())
        {
            var elevatedProcess = RelaunchElevated("--register");
            if (elevatedProcess is null)
            {
                ShowError("App Tunnel Portable", "The elevated setup process could not be started.");
                return;
            }

            elevatedProcess.WaitForExit();
            if (elevatedProcess.ExitCode != 0)
            {
                ShowWarning(
                    "App Tunnel Portable",
                    "Portable setup did not complete successfully. App Tunnel will open in limited mode without the service.");
            }
        }
        else
        {
            try
            {
                serviceRegistration.EnsureRegistered();
                serviceRegistration.EnsureStarted();
            }
            catch (Exception ex)
            {
                ShowWarning(
                    "App Tunnel Portable",
                    $"Portable setup did not complete successfully. App Tunnel will open in limited mode without the service.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            }
        }
    }

    var uiPath = deploymentPlan.UiExecutablePath;
    if (!File.Exists(uiPath))
    {
        throw new FileNotFoundException("The portable runtime is incomplete. AppTunnel.UI.exe was not found.", uiPath);
    }

    Process.Start(new ProcessStartInfo
    {
        FileName = uiPath,
        WorkingDirectory = Path.GetDirectoryName(uiPath)!,
        UseShellExecute = true,
    });
}
catch (Exception ex)
{
    ShowError("App Tunnel Portable", ex.Message);
    Environment.ExitCode = 1;
}

static Process? RelaunchElevated(string arguments)
{
    try
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable."),
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
        });
    }
    catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
    {
        return null;
    }
}

static void ShowError(string title, string message) =>
    NativeMethods.MessageBoxW(IntPtr.Zero, message, title, 0x10);

static void ShowWarning(string title, string message) =>
    NativeMethods.MessageBoxW(IntPtr.Zero, message, title, 0x30);

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    internal static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
