namespace AppTunnel.Deployment;

public sealed record PortableDeploymentPlan(
    string RootDirectory,
    string RuntimeDirectory,
    string DataDirectory,
    string LogsDirectory,
    ServiceRegistrationOptions ServiceOptions,
    string UiExecutablePath,
    string CleanupExecutablePath);

public static class PortableDeploymentPlanFactory
{
    public static PortableDeploymentPlan Create(string rootDirectory) =>
        new(
            RootDirectory: rootDirectory,
            RuntimeDirectory: PortableLayout.GetRuntimeDirectory(rootDirectory),
            DataDirectory: PortableLayout.GetDataDirectory(rootDirectory),
            LogsDirectory: PortableLayout.GetLogsDirectory(rootDirectory),
            ServiceOptions: new ServiceRegistrationOptions(
                ServiceName: "AppTunnelPortableService",
                DisplayName: "App Tunnel Portable Service",
                Description: "Portable App Tunnel service host. Networking components still require admin rights.",
                BinaryPath: PortableLayout.GetServiceExecutablePath(rootDirectory),
                Arguments: $"--root \"{rootDirectory}\" --portable"),
            UiExecutablePath: PortableLayout.GetUiExecutablePath(rootDirectory),
            CleanupExecutablePath: Path.Combine(rootDirectory, "AppTunnelPortableCleanup.exe"));
}
