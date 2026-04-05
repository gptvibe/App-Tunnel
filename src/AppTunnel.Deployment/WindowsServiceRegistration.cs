using System.Diagnostics;
using System.ServiceProcess;
using System.Security.Principal;

namespace AppTunnel.Deployment;

public sealed record ServiceRegistrationOptions(
    string ServiceName,
    string DisplayName,
    string Description,
    string BinaryPath,
    string Arguments);

public sealed class WindowsServiceRegistration(ServiceRegistrationOptions options)
{
    public bool Exists() =>
        ServiceController.GetServices()
            .Any(service => string.Equals(service.ServiceName, options.ServiceName, StringComparison.OrdinalIgnoreCase));

    public bool IsRunning()
    {
        using var controller = FindController();
        return controller is not null && controller.Status == ServiceControllerStatus.Running;
    }

    public void EnsureRegistered()
    {
        if (!IsAdministrator())
        {
            throw new InvalidOperationException("Administrator rights are required to register the Windows service.");
        }

        if (Exists())
        {
            RunSc(
                "config",
                options.ServiceName,
                "binPath=",
                BuildBinaryPath(),
                "start=",
                "auto",
                "displayname=",
                options.DisplayName);
        }
        else
        {
            RunSc(
                "create",
                options.ServiceName,
                "binPath=",
                BuildBinaryPath(),
                "start=",
                "auto",
                "displayname=",
                options.DisplayName);
        }

        RunSc("description", options.ServiceName, options.Description);
    }

    public void EnsureStarted()
    {
        if (!Exists())
        {
            throw new InvalidOperationException($"Service '{options.ServiceName}' is not registered.");
        }

        using var controller = FindController();
        if (controller is null || controller.Status == ServiceControllerStatus.Running)
        {
            return;
        }

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
    }

    public void StopIfRunning()
    {
        using var controller = FindController();
        if (controller is null)
        {
            return;
        }

        if (controller.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
        {
            return;
        }

        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
    }

    public void Unregister()
    {
        if (!IsAdministrator())
        {
            throw new InvalidOperationException("Administrator rights are required to unregister the Windows service.");
        }

        if (!Exists())
        {
            return;
        }

        StopIfRunning();
        RunSc("delete", options.ServiceName);
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private ServiceController? FindController() =>
        ServiceController.GetServices()
            .FirstOrDefault(service => string.Equals(service.ServiceName, options.ServiceName, StringComparison.OrdinalIgnoreCase));

    private string BuildBinaryPath() =>
        string.IsNullOrWhiteSpace(options.Arguments)
            ? $"\"{options.BinaryPath}\""
            : $"\"{options.BinaryPath}\" {options.Arguments}";

    private static void RunSc(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start sc.exe.");

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            var output = process.StandardOutput.ReadToEnd();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        }
    }
}
