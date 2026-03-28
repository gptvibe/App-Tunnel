namespace AppTunnel.Core.Contracts;

public interface ILogBundleExporter
{
    Task<string> ExportAsync(string destinationDirectory, CancellationToken cancellationToken);
}