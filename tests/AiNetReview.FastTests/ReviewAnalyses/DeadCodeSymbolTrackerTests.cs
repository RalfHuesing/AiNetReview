namespace AiNetReview.FastTests.ReviewAnalyses;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AiNetReview.Core.ReviewAnalyses.DeadCodeCandidates;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed class DeadCodeSymbolTrackerTests
{
    [Fact]
    public void Protect_TracksSymbolAndContainingType()
    {
        var (typeSymbol, methodSymbol) = GetTypeAndMethod("""
            namespace Sample;
            public class Service
            {
                public void Execute() { }
            }
            """);

        var tracker = new DeadCodeSymbolTracker();
        tracker.Protect(methodSymbol);

        Assert.True(tracker.IsProtected(methodSymbol));
        Assert.True(tracker.IsProtected(typeSymbol));
        Assert.False(tracker.HasUncertainty(methodSymbol));
        Assert.Equal(2, tracker.ProtectedSymbols.Count);
    }

    [Fact]
    public void Protect_WithoutContainingType_TracksOnlySymbol()
    {
        var typeSymbol = GetType("""
            namespace Sample;
            public class Standalone { }
            """);

        var tracker = new DeadCodeSymbolTracker();
        tracker.Protect(typeSymbol);

        Assert.True(tracker.IsProtected(typeSymbol));
        Assert.Single(tracker.ProtectedSymbols);
    }

    [Fact]
    public void MarkUncertain_TracksUncertaintyWithoutProtecting()
    {
        var (typeSymbol, methodSymbol) = GetTypeAndMethod("""
            namespace Sample;
            public class Service
            {
                public void Execute() { }
            }
            """);

        var tracker = new DeadCodeSymbolTracker();
        tracker.MarkUncertain(methodSymbol);

        Assert.True(tracker.HasUncertainty(methodSymbol));
        Assert.False(tracker.IsProtected(methodSymbol));
        Assert.False(tracker.IsProtected(typeSymbol));
        Assert.Single(tracker.UncertainSymbols);
        Assert.Empty(tracker.ProtectedSymbols);
    }

    [Fact]
    public void UnionWith_MergesMarkupUsageProtectedAndUncertainSymbols()
    {
        var (typeSymbol, methodSymbol) = GetTypeAndMethod("""
            namespace Sample;
            public class Service
            {
                public void Execute() { }
            }
            """);

        var tracker = new DeadCodeSymbolTracker();
        var markupUsage = new DeadCodeMarkupUsage(
            [typeSymbol],
            [methodSymbol]);

        tracker.UnionWith(markupUsage);

        Assert.True(tracker.IsProtected(typeSymbol));
        Assert.True(tracker.HasUncertainty(methodSymbol));
    }

    private static (INamedTypeSymbol TypeSymbol, IMethodSymbol MethodSymbol) GetTypeAndMethod(string source)
    {
        var (typeSymbol, methodSymbol) = GetSymbols(source);
        return (typeSymbol, methodSymbol ?? throw new InvalidOperationException("Method symbol was not found."));
    }

    private static INamedTypeSymbol GetType(string source) => GetSymbols(source).TypeSymbol;

    private static (INamedTypeSymbol TypeSymbol, IMethodSymbol? MethodSymbol) GetSymbols(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "DeadCodeSymbolTrackerTests",
            [tree],
            PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);

        var typeDeclaration = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().First();
        var typeSymbol = (INamedTypeSymbol)semanticModel.GetDeclaredSymbol(typeDeclaration)!;
        var methodDeclaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var methodSymbol = methodDeclaration is not null ? (IMethodSymbol)semanticModel.GetDeclaredSymbol(methodDeclaration)! : null;

        return (typeSymbol, methodSymbol);
    }

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(static assemblyPath => MetadataReference.CreateFromFile(assemblyPath));
}
