using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Contracts;

public interface IStructuredLogService
{
    Task WriteAsync(
        string level,
        string source,
        string message,
        IReadOnlyDictionary<string, string>? properties,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StructuredLogEntry>> ReadRecentAsync(
        int maxEntries,
        CancellationToken cancellationToken);
}
