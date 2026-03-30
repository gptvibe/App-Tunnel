using System.Diagnostics;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;

namespace AppTunnel.Service.Runtime;

public sealed class WindowsApplicationDiscoveryService : IApplicationDiscoveryService
{
    public Task<DiscoveredApplication> InspectAsync(
        AppKind appKind,
        string? executablePath,
        string? packageFamilyName,
        string? packageIdentity,
        string? displayName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return appKind switch
        {
            AppKind.Win32Exe => Task.FromResult(InspectWin32Executable(executablePath, displayName)),
            AppKind.PackagedApp => throw new NotSupportedException("Packaged-app rule creation is not implemented yet."),
            _ => throw new InvalidOperationException($"Unsupported app kind '{appKind}'."),
        };
    }

    public Task<IReadOnlyList<DiscoveredApplication>> EnumeratePackagedApplicationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DiscoveredApplication>>([]);
    }

    private static DiscoveredApplication InspectWin32Executable(string? executablePath, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        }

        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The executable path does not exist.", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Win32 app rules require an .exe path.");
        }

        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? ResolveDisplayName(fullPath)
            : displayName.Trim();

        return new DiscoveredApplication(
            AppKind.Win32Exe,
            resolvedDisplayName,
            fullPath,
            PackageFamilyName: null,
            PackageIdentity: null);
    }

    private static string ResolveDisplayName(string executablePath)
    {
        var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);

        if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
        {
            return versionInfo.FileDescription.Trim();
        }

        return Path.GetFileNameWithoutExtension(executablePath);
    }
}