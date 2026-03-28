using System.Text;
using System.Text.Json;
using AppTunnel.Core.Contracts;
using AppTunnel.Core.Domain;
using AppTunnel.Core.Ipc;

namespace AppTunnel.Core.Services;

public sealed class StructuredLogService(AppTunnelPaths paths, string componentName) : IStructuredLogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task WriteAsync(
        string level,
        string source,
        string message,
        IReadOnlyDictionary<string, string>? properties,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            throw new ArgumentException("A log level is required.", nameof(level));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("A log source is required.", nameof(source));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A log message is required.", nameof(message));
        }

        cancellationToken.ThrowIfCancellationRequested();
        paths.EnsureDirectories();

        var entry = new StructuredLogEntry(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            level,
            source,
            message,
            properties is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase));

        var filePath = Path.Combine(
            paths.LogsDirectory,
            $"{DateTime.UtcNow:yyyyMMdd}-{SanitizeComponent(componentName)}.ndjson");

        var line = JsonSerializer.Serialize(entry, AppTunnelJson.Default);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteLineAsync(line);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StructuredLogEntry>> ReadRecentAsync(
        int maxEntries,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        paths.EnsureDirectories();

        if (maxEntries <= 0)
        {
            return [];
        }

        var entries = new List<StructuredLogEntry>();
        var files = Directory
            .EnumerateFiles(paths.LogsDirectory, "*.ndjson", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(10);

        foreach (var file in files)
        {
            foreach (var line in await File.ReadAllLinesAsync(file, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var entry = JsonSerializer.Deserialize<StructuredLogEntry>(line, AppTunnelJson.Default);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
        }

        return entries
            .OrderByDescending(entry => entry.TimestampUtc)
            .Take(maxEntries)
            .ToArray();
    }

    private static string SanitizeComponent(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) ? '-' : char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
