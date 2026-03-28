namespace AppTunnel.Core.Domain;

public enum AppKind
{
    Win32Exe,
    PackagedApp,
}

public enum VpnProviderKind
{
    WireGuard,
    OpenVpn,
}

public enum RouterBackendKind
{
    MvpRouter,
    ProdRouter,
}

public enum DistributionMode
{
    Installer,
    PortableZip,
}

public enum BackendReadiness
{
    Planned,
    Mvp,
    ProductionReady,
}

public enum SecretPurpose
{
    WireGuardPrivateKey,
    OpenVpnCredentials,
    ProfileBlob,
}