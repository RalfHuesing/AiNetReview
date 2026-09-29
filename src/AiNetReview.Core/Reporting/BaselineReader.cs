namespace AiNetReview.Core.Reporting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;

/// <summary>Reads and validates the optional baseline used to select changed source files.</summary>
public sealed class BaselineReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<IReadOnlyDictionary<string, string>?> ReadAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var baselinePath = Path.Combine(outputDirectory, "baseline.json");
        if (!File.Exists(baselinePath))
        {
            return null;
        }

        await using var stream = new FileStream(baselinePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        SourceBaselineDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<SourceBaselineDocument>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The source baseline is not valid JSON.", exception);
        }

        if (document is null || document.SchemaVersion != 1 || document.Files is null)
        {
            throw new InvalidDataException("The source baseline has an unsupported or invalid schema.");
        }

        var files = new Dictionary<string, string>(PathComparer);
        foreach (var file in document.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file is null || !IsCanonicalRelativePath(file.Path) || !IsSha256(file.Sha256) || !files.TryAdd(file.Path, file.Sha256))
            {
                throw new InvalidDataException("The source baseline contains an invalid or duplicate file record.");
            }
        }

        return files;
    }

    private static bool IsCanonicalRelativePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path)
        && !path.Contains('\\')
        && !path.Contains(':')
        && !path.Split('/').Any(static segment => segment.Length == 0 || segment is "." or "..");

    private static bool IsSha256(string? hash) =>
        hash is { Length: 64 }
        && hash.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
