using System.IO.Compression;
using System.Text.Json;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;

namespace AppTunnel.Core.Services;

public sealed class LogBundleExporter(
    AppTunnelPaths paths,
    IAppTunnelConfigurationStore configurationStore) : ILogBundleExporter
{
    public async Task<ExportedLogBundle> ExportAsync(
        string? destinationDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        paths.EnsureDirectories();

        var exportRoot = string.IsNullOrWhiteSpace(destinationDirectory)
            ? paths.ExportsDirectory
            : Path.GetFullPath(destinationDirectory);

        Directory.CreateDirectory(exportRoot);

        var timestamp = DateTimeOffset.UtcNow;
        var stagingDirectory = Path.Combine(
            exportRoot,
            $"apptunnel-bundle-{timestamp:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(stagingDirectory);

        var configuration = await configurationStore.LoadAsync(cancellationToken);
        var metadataPath = Path.Combine(stagingDirectory, "metadata.json");
        var configurationPath = Path.Combine(stagingDirectory, "config.json");
        var logsDirectory = Path.Combine(stagingDirectory, "logs");
        Directory.CreateDirectory(logsDirectory);

        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(
                new
                {
                    exportedAtUtc = timestamp,
                    rootDirectory = paths.RootDirectory,
                },
                new JsonSerializerOptions(AppTunnelJson.Default)
                {
                    WriteIndented = true,
                }),
            cancellationToken);

        await File.WriteAllTextAsync(
            configurationPath,
            JsonSerializer.Serialize(
                configuration,
                new JsonSerializerOptions(AppTunnelJson.Default)
                {
                    WriteIndented = true,
                }),
            cancellationToken);

        var copiedFileCount = 0;
        foreach (var logFile in Directory.EnumerateFiles(paths.LogsDirectory, "*.ndjson", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(logFile, Path.Combine(logsDirectory, Path.GetFileName(logFile)), overwrite: true);
            copiedFileCount++;
        }

        var bundlePath = Path.Combine(exportRoot, $"AppTunnel-logs-{timestamp:yyyyMMdd-HHmmss}.zip");
        if (File.Exists(bundlePath))
        {
            File.Delete(bundlePath);
        }

        ZipFile.CreateFromDirectory(stagingDirectory, bundlePath);
        Directory.Delete(stagingDirectory, recursive: true);

        return new ExportedLogBundle(
            bundlePath,
            timestamp,
            copiedFileCount + 2);
    }
}
