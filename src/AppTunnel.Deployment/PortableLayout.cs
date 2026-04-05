namespace AppTunnel.Deployment;

public static class PortableLayout
{
    public const string RuntimeDirectoryName = "runtime";
    public const string DataDirectoryName = "data";
    public const string LogsDirectoryName = "logs";

    public static string GetRootDirectory() =>
        Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public static string GetRuntimeDirectory(string rootDirectory) =>
        Path.Combine(rootDirectory, RuntimeDirectoryName);

    public static string GetDataDirectory(string rootDirectory) =>
        Path.Combine(rootDirectory, DataDirectoryName);

    public static string GetLogsDirectory(string rootDirectory) =>
        Path.Combine(rootDirectory, LogsDirectoryName);

    public static string GetUiExecutablePath(string rootDirectory) =>
        Path.Combine(GetRuntimeDirectory(rootDirectory), "AppTunnel.UI.exe");

    public static string GetServiceExecutablePath(string rootDirectory) =>
        Path.Combine(GetRuntimeDirectory(rootDirectory), "AppTunnel.Service.exe");

    public static string GetBridgeExecutablePath(string rootDirectory) =>
        Path.Combine(GetRuntimeDirectory(rootDirectory), "router", "AppTunnel.Router.WfpBridge.exe");

    public static string GetWfpStatePath(string rootDirectory) =>
        Path.Combine(GetRuntimeDirectory(rootDirectory), "router", "AppTunnel.WfpState.json");

    public static void EnsureLayout(string rootDirectory)
    {
        Directory.CreateDirectory(GetRuntimeDirectory(rootDirectory));
        Directory.CreateDirectory(GetDataDirectory(rootDirectory));
        Directory.CreateDirectory(GetLogsDirectory(rootDirectory));
    }
}
