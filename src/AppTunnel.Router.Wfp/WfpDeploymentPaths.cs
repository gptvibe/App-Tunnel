namespace AppTunnel.Router.Wfp;

internal sealed record WfpDeploymentPaths(
    string DriverServiceName,
    string DriverDisplayName,
    string DriverBinaryPath,
    string BridgeBinaryPath,
    string StateFilePath)
{
    public static WfpDeploymentPaths Resolve()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidateRoots = new[]
        {
            baseDirectory,
            Path.GetFullPath(Path.Combine(baseDirectory, "..")),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..")),
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        var driverBinaryPath = ResolveFirstExisting(candidateRoots, Path.Combine("router", "AppTunnel.Router.WfpDriver.sys"))
            ?? ResolveFirstExisting(candidateRoots, "AppTunnel.Router.WfpDriver.sys")
            ?? ResolveFirstExisting(candidateRoots, Path.Combine("native", "bin", "AppTunnel.Router.WfpDriver", "x64", "Release", "AppTunnel.Router.WfpDriver.sys"))
            ?? Path.Combine(baseDirectory, "router", "AppTunnel.Router.WfpDriver.sys");

        var bridgeBinaryPath = ResolveFirstExisting(candidateRoots, Path.Combine("router", "AppTunnel.Router.WfpBridge.exe"))
            ?? ResolveFirstExisting(candidateRoots, "AppTunnel.Router.WfpBridge.exe")
            ?? ResolveFirstExisting(candidateRoots, Path.Combine("native", "bin", "AppTunnel.Router.WfpBridge", "x64", "Release", "AppTunnel.Router.WfpBridge.exe"))
            ?? Path.Combine(baseDirectory, "router", "AppTunnel.Router.WfpBridge.exe");
        var stateDirectory = Path.GetDirectoryName(bridgeBinaryPath)
            ?? Path.Combine(baseDirectory, "router");

        return new WfpDeploymentPaths(
            DriverServiceName: "AppTunnelWfp",
            DriverDisplayName: "App Tunnel WFP Driver",
            DriverBinaryPath: driverBinaryPath,
            BridgeBinaryPath: bridgeBinaryPath,
            StateFilePath: Path.Combine(stateDirectory, "AppTunnel.WfpState.json"));
    }

    private static string? ResolveFirstExisting(IEnumerable<string> roots, string relativePath)
    {
        foreach (var root in roots)
        {
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
