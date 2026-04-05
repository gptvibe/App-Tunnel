using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AppTunnel.Deployment;

try
{
    var rootDirectory = PortableLayout.GetRootDirectory();
    var quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);
    var deploymentPlan = PortableDeploymentPlanFactory.Create(rootDirectory);

    if (!WindowsServiceRegistration.IsAdministrator())
    {
        RelaunchElevated(quiet ? "--quiet" : string.Empty, quiet);
        return;
    }

    var serviceRegistration = new WindowsServiceRegistration(
        deploymentPlan.ServiceOptions);

    serviceRegistration.Unregister();
    WfpDeploymentCleanup.UninstallIfPresent(rootDirectory);

    var logsDirectory = PortableLayout.GetLogsDirectory(rootDirectory);
    if (Directory.Exists(logsDirectory))
    {
        foreach (var file in Directory.GetFiles(logsDirectory))
        {
            File.Delete(file);
        }
    }

    if (!quiet)
    {
        ShowInfo("App Tunnel Portable", "Portable App Tunnel service and WFP state removed.");
    }
}
catch (Exception ex)
{
    ShowError("App Tunnel Portable Cleanup", ex.Message);
    Environment.ExitCode = 1;
}

static void RelaunchElevated(string arguments, bool quiet)
{
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Process path is unavailable."),
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
        });
    }
    catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
    {
        if (!quiet)
        {
            ShowError("App Tunnel Portable Cleanup", "Administrator approval is required to remove the portable networking components.");
        }
    }
}

static void ShowInfo(string title, string message) =>
    NativeMethods.MessageBoxW(IntPtr.Zero, message, title, 0x40);

static void ShowError(string title, string message) =>
    NativeMethods.MessageBoxW(IntPtr.Zero, message, title, 0x10);

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    internal static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
