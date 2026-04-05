using System.Diagnostics;

namespace AppTunnel.Deployment;

public static class WfpDeploymentCleanup
{
    public static void UninstallIfPresent(string portableRoot)
    {
        var bridgePath = PortableLayout.GetBridgeExecutablePath(portableRoot);
        if (File.Exists(bridgePath))
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = bridgePath,
                Arguments = "uninstall",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            process?.WaitForExit(20000);
        }

        var statePath = PortableLayout.GetWfpStatePath(portableRoot);
        if (File.Exists(statePath))
        {
            File.Delete(statePath);
        }
    }
}
