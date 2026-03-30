namespace AppTunnel.Core.Domain;

public enum AppKind
{
    Win32Exe,
    PackagedApp,
}

public enum TunnelKind
{
    WireGuard,
    OpenVpn,
}

public enum RoutingBackendKind
{
    DryRun,
    WinDivert,
    Wfp,
}

public enum DistributionMode
{
    Installer,
    PortableZip,
}

public enum BackendReadiness
{
    Planned,
    DryRun,
    Mvp,
    ProductionReady,
}

public enum SecretPurpose
{
    WireGuardPrivateKey,
    OpenVpnCredentials,
    ProfileBlob,
}

public enum ServiceRunState
{
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted,
}

public enum TunnelConnectionState
{
    Unknown,
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Faulted,
}
