namespace AiNetReview.FastTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;

internal static class FastTestReferences
{
    internal static MetadataReference[] CreatePlatformReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return FilterPlatformAssemblyPaths(paths)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    internal static string[] FilterPlatformAssemblyPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths
            .Where(static path => !ReviewSourceClassifier.IsTestReferenceAssembly(path))
            .ToArray();
    }
}
