namespace AiNetReview.Core.ReviewAnalyses.DeadCodeCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>Collects recognized indirect bindings from loaded C# source.</summary>
internal sealed class DeadCodeIndirectUsageIndex
{
    private static readonly string[] FixedEntryPointAttributes =
    [
        "System.Runtime.CompilerServices.ModuleInitializerAttribute",
        "Microsoft.JSInterop.JSInvokableAttribute",
    ];

    private readonly DeadCodeSymbolTracker symbols = new();

    private DeadCodeIndirectUsageIndex()
    {
    }

    public bool IsProtected(ISymbol symbol) => symbols.IsProtected(symbol);

    public bool HasUncertainty(ISymbol symbol) => symbols.HasUncertainty(symbol);

    private void Merge(DeadCodeMarkupUsage markupUsage) => symbols.UnionWith(markupUsage);

    public static async Task<DeadCodeIndirectUsageIndex> CreateAsync(
        ReviewContext context,
        IReadOnlyList<string> configuredAttributes,
        CancellationToken cancellationToken)
    {
        var index = new DeadCodeIndirectUsageIndex();
        var projects = context.Solution.Projects
            .Where(static project => project.Language == LanguageNames.CSharp)
            .ToArray();
        var compilations = new List<Compilation>(projects.Length);
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AnalysisFailedException($"Compilation could not be created for project '{project.Name}'.");
            compilations.Add(compilation);
            var fixedAttributeTypes = FixedEntryPointAttributes
                .Select(compilation.GetTypeByMetadataName)
                .Where(static type => type is not null)
                .Cast<INamedTypeSymbol>()
                .Where(static type => !IsSourceSymbol(type));
            var configuredAttributeTypes = configuredAttributes
                .Select(compilation.GetTypeByMetadataName)
                .Where(static type => type is not null)
                .Cast<INamedTypeSymbol>();
            var attributeTypes = fixedAttributeTypes.Concat(configuredAttributeTypes)
                .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
                .ToArray();

            var documents = project.Documents
                .Concat((await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false)).Cast<Document>())
                .ToArray();
            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Syntax could not be read for document '{document.Name}'.");
                var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new AnalysisFailedException($"Semantic model could not be created for document '{document.Name}'.");
                index.CollectEntryPointAttributes(root, model, attributeTypes, cancellationToken);
                index.CollectInvocations(root, model, cancellationToken);
            }
        }

        var markupUsage = await DeadCodeMarkupUsageCollector.CollectAsync(
            context, projects, compilations, cancellationToken).ConfigureAwait(false);
        index.Merge(markupUsage);

        return index;
    }

    private void CollectEntryPointAttributes(
        SyntaxNode root,
        SemanticModel model,
        IReadOnlyList<INamedTypeSymbol> attributeTypes,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().Where(static node =>
                     node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or MethodDeclarationSyntax))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = model.GetDeclaredSymbol(declaration, cancellationToken);
            if (symbol is null)
            {
                continue;
            }

            if (HasEntryPointAttribute(symbol, attributeTypes)
                || symbol is IMethodSymbol method && HasFrameworkEntryPointAttribute(method))
            {
                Protect(symbol);
                continue;
            }
        }
    }

    private void CollectInvocations(SyntaxNode root, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (model.GetSymbolInfo(call, cancellationToken).Symbol is not IMethodSymbol method)
            {
                continue;
            }

            if (IsReflectionMethod(method))
            {
                CollectReflection(call, model, method, cancellationToken);
            }

            if (IsDependencyRegistration(method))
            {
                CollectRegistration(call, model, method, cancellationToken);
            }

            if (IsMiddlewareRegistration(method))
            {
                foreach (var type in method.TypeArguments.OfType<INamedTypeSymbol>())
                {
                    ProtectFrameworkHooks(type);
                    foreach (var candidate in type.GetMembers().OfType<IMethodSymbol>().Where(static candidate =>
                                 candidate.Name is "Invoke" or "InvokeAsync"))
                    {
                        if (!candidate.IsStatic && candidate.Parameters.FirstOrDefault()?.Type.ToDisplayString() == "Microsoft.AspNetCore.Http.HttpContext")
                        {
                            Protect(candidate);
                        }
                    }
                }
            }

        }
    }

    private void CollectReflection(InvocationExpressionSyntax call, SemanticModel model, IMethodSymbol method, CancellationToken cancellationToken)
    {
        if (method.ContainingType.ToDisplayString() == "System.Reflection.Assembly" && method.Name == "GetTypes")
        {
            foreach (var type in DeadCodeSourceTypeEnumerator.SourceTypes(model.Compilation.Assembly.GlobalNamespace))
            {
                MarkUncertain(type);
            }

            return;
        }

        if (call.Expression is not MemberAccessExpressionSyntax access
            || access.Expression is not TypeOfExpressionSyntax typeOf
            || model.GetTypeInfo(typeOf.Type, cancellationToken).Type is not INamedTypeSymbol targetType)
        {
            return;
        }

        var relevantName = method.Name is "GetMethod" or "GetMethods" or "GetProperty" or "GetProperties";
        if (!relevantName)
        {
            return;
        }

        var expectedKind = method.Name.StartsWith("GetMethod", StringComparison.Ordinal)
            ? SymbolKind.Method
            : SymbolKind.Property;
        var hasNameParameter = method.Parameters.FirstOrDefault()?.Type.SpecialType == SpecialType.System_String;
        var nameArgument = hasNameParameter ? call.ArgumentList.Arguments.FirstOrDefault() : null;
        var names = nameArgument is null ? null : model.GetConstantValue(nameArgument.Expression, cancellationToken).Value as string;
        var members = targetType.GetMembers().Where(member => member.Kind == expectedKind
            && (names is null || StringComparer.Ordinal.Equals(member.Name, names)));
        foreach (var member in members)
        {
            if (!hasNameParameter || names is not null)
            {
                Protect(member);
            }
            else
            {
                MarkUncertain(member);
            }
        }
    }

    private void CollectRegistration(InvocationExpressionSyntax call, SemanticModel model, IMethodSymbol method, CancellationToken cancellationToken)
    {
        var types = method.TypeArguments.OfType<INamedTypeSymbol>().ToList();
        types.AddRange(call.ArgumentList.Arguments
            .Select(argument => model.GetTypeInfo(argument.Expression, cancellationToken).Type)
            .OfType<INamedTypeSymbol>()
            .Where(static type => type.TypeKind != TypeKind.Error));
        foreach (var type in types.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            ProtectFrameworkHooks(type);
        }
    }

    private static bool IsReflectionMethod(IMethodSymbol method) =>
        !IsSourceSymbol(method)
        && ((StringComparer.Ordinal.Equals(method.ContainingType.ToDisplayString(), "System.Type")
                && (method.Name is "GetMethod" or "GetMethods" or "GetProperty" or "GetProperties"))
            || (StringComparer.Ordinal.Equals(method.ContainingType.ToDisplayString(), "System.Reflection.Assembly")
                && StringComparer.Ordinal.Equals(method.Name, "GetTypes")));

    private static bool IsDependencyRegistration(IMethodSymbol method) =>
        method.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection"
        && (method.ContainingType.Name is "ServiceCollectionServiceExtensions"
            or "ServiceCollectionHostedServiceExtensions"
            or "OptionsServiceCollectionExtensions")
        && !IsSourceSymbol(method)
        && (method.Name is "AddSingleton" or "AddScoped" or "AddTransient" or "AddHostedService" or "ConfigureOptions");

    private static bool IsMiddlewareRegistration(IMethodSymbol method) =>
        method.ContainingType.ToDisplayString() == "Microsoft.AspNetCore.Builder.UseMiddlewareExtensions"
        && !IsSourceSymbol(method)
        && method.Name == "UseMiddleware";

    private void ProtectFrameworkHooks(INamedTypeSymbol type)
    {
        Protect(type);
        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            if (method.OverriddenMethod is { } overridden && !IsSourceSymbol(overridden))
            {
                Protect(method);
            }

            foreach (var contract in type.AllInterfaces.SelectMany(static contract => contract.GetMembers()))
            {
                if (!IsSourceSymbol(contract)
                    && SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(contract), method))
                {
                    Protect(method);
                }
            }
        }
    }

    private void Protect(ISymbol symbol) => symbols.Protect(symbol);

    private void MarkUncertain(ISymbol symbol) => symbols.MarkUncertain(symbol);

    private static bool HasEntryPointAttribute(ISymbol symbol, IReadOnlyList<INamedTypeSymbol> attributeTypes) =>
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass is { } actual
            && attributeTypes.Any(expected => SymbolEqualityComparer.Default.Equals(actual, expected)));

    private static bool HasFrameworkEntryPointAttribute(IMethodSymbol method) => method.GetAttributes().Any(attribute =>
        attribute.AttributeClass is { } type
        && type.ContainingAssembly.Name.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal)
        && !IsSourceSymbol(type)
        && type.ToDisplayString() is "Microsoft.AspNetCore.Mvc.HttpGetAttribute"
            or "Microsoft.AspNetCore.Mvc.HttpPostAttribute"
            or "Microsoft.AspNetCore.Mvc.HttpPutAttribute"
            or "Microsoft.AspNetCore.Mvc.HttpDeleteAttribute"
            or "Microsoft.AspNetCore.Mvc.AcceptVerbsAttribute"
            or "Microsoft.AspNetCore.Mvc.RouteAttribute");

    private static bool IsSourceSymbol(ISymbol symbol) => symbol.Locations.Any(static location => location.IsInSource);

}
