namespace AiNetReview.Core.Reporting;

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.Configuration;

/// <summary>Safely replaces the current date-based source baseline.</summary>
public sealed class BaselineWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly Func<CancellationToken, ValueTask>? beforePublication;

    public BaselineWriter()
    {
    }

    internal BaselineWriter(Func<CancellationToken, ValueTask> beforePublication)
    {
        this.beforePublication = beforePublication ?? throw new ArgumentNullException(nameof(beforePublication));
    }

    public async Task<string> WriteAsync(
        ReviewConfig config,
        LoadedSolution loadedSolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(loadedSolution);
        cancellationToken.ThrowIfCancellationRequested();

        var baselinePath = Path.Combine(config.ResolvedOutputDirectory, "baseline.json");
        var temporaryPath = Path.Combine(config.ResolvedOutputDirectory, $".baseline-{Guid.NewGuid():N}.tmp");
        var document = new BaselineDocument(1, loadedSolution.SourceFiles);
        var published = false;
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (beforePublication is not null)
            {
                await beforePublication(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, baselinePath, overwrite: true);
            published = true;
            return Path.Combine(config.OutputDirectory, "baseline.json").Replace('\\', '/');
        }
        finally
        {
            if (!published && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record BaselineDocument(int SchemaVersion, System.Collections.Generic.IReadOnlyList<SourceFileSnapshot> Files);
}
