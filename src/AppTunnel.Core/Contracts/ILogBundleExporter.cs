using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface ILogBundleExporter
{
    Task<ExportedLogBundle> ExportAsync(
        string? destinationDirectory,
        CancellationToken cancellationToken);
}
