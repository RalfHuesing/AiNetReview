namespace AiNetReview.Core.Rules.DeadCodeCandidates;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>Collects recognized indirect bindings from the loaded C# and markup snapshot.</summary>
internal sealed class DeadCodeIndirectUsageIndex
{
    private static readonly string[] FixedEntryPointAttributes =
    [
        "System.Runtime.CompilerServices.ModuleInitializerAttribute",
        "Microsoft.JSInterop.JSInvokableAttribute",
    ];

    private readonly HashSet<ISymbol> protectedSymbols = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> uncertainSymbols = new(SymbolEqualityComparer.Default);

    private DeadCodeIndirectUsageIndex()
    {
    }

    public bool IsProtected(ISymbol symbol) => protectedSymbols.Contains(Normalize(symbol));

    public bool HasUncertainty(ISymbol symbol) => uncertainSymbols.Contains(Normalize(symbol));

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

        foreach (var markup in context.MarkupDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = ResolveMarkupProject(projects, markup.FilePath, context.ProjectRoot);
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AnalysisFailedException($"Compilation could not be created for project '{project.Name}'.");
            try
            {
                switch (Path.GetExtension(markup.FilePath).ToLowerInvariant())
                {
                    case ".xaml":
                        index.CollectXaml(markup, compilation);
                        break;
                    case ".razor":
                        index.CollectRazor(markup, compilation, compilations);
                        break;
                    case ".js":
                        index.CollectJavaScript(markup, compilations);
                        break;
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                throw new AnalysisFailedException($"Markup could not be evaluated safely: '{markup.FilePath}'.", ex);
            }
        }

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
            foreach (var type in SourceTypes(model.Compilation.Assembly.GlobalNamespace))
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

    private void CollectXaml(MarkupDocumentSnapshot markup, Compilation compilation)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(markup.Text, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new AnalysisFailedException($"XAML markup could not be evaluated: '{markup.FilePath}'.", ex);
        }

        var root = document.Root;
        if (root is null)
        {
            throw new AnalysisFailedException($"XAML markup has no document element: '{markup.FilePath}'.");
        }

        var codeBehindName = root.Attributes().FirstOrDefault(static attribute => attribute.Name.LocalName == "Class")?.Value;
        var codeBehind = codeBehindName is null ? null : compilation.GetTypeByMetadataName(codeBehindName);
        if (codeBehindName is not null && codeBehind is null)
        {
            MarkTypesUncertain(codeBehindName.Split('.').Last(), compilation);
        }

        if (codeBehind is not null)
        {
            Protect(codeBehind);
        }

        foreach (var element in root.DescendantsAndSelf())
        {
            var type = ResolveXamlType(element.Name, compilation);
            if (type is not null)
            {
                Protect(type);
            }

            foreach (var attribute in element.Attributes())
            {
                BindXamlStatic(attribute.Value, element, compilation);
                BindXamlPath(attribute.Value, root, compilation);
                var attributeName = attribute.Name.LocalName;
                if (codeBehind is not null && type?.GetMembers(attributeName).Any(static member => member is IEventSymbol) == true)
                {
                    var handlers = codeBehind.GetMembers(attribute.Value).OfType<IMethodSymbol>().ToArray();
                    if (handlers.Length == 1) Protect(handlers[0]);
                    else if (handlers.Length == 0) MarkUncertainMethods(attribute.Value, compilation);
                    else foreach (var handler in handlers) MarkUncertain(handler);
                }
            }
        }
    }

    private void BindXamlStatic(string value, XElement element, Compilation compilation)
    {
        foreach (Match match in Regex.Matches(value, @"\{x:Static\s+(\w+):(\w+)\.(\w+)\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            var xmlNamespace = element.GetNamespaceOfPrefix(match.Groups[1].Value)?.NamespaceName;
            var type = xmlNamespace is null ? null : ResolveXamlType(xmlNamespace, match.Groups[2].Value, compilation);
            if (type is null)
            {
                MarkTypesUncertain(match.Groups[2].Value, compilation);
                continue;
            }

            var members = type.GetMembers(match.Groups[3].Value).ToArray();
            if (members.Length == 0) MarkUncertainMembers(match.Groups[3].Value, compilation);
            else foreach (var member in members) Protect(member);
        }
    }

    private void BindXamlPath(string value, XElement root, Compilation compilation)
    {
        foreach (Match match in Regex.Matches(value, @"\{Binding\s+(?:Path=)?([\w.]+)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            var path = match.Groups[1].Value;
            var typeName = root.DescendantsAndSelf()
                .SelectMany(static element => element.Elements())
                .FirstOrDefault(static element => element.Name.LocalName.EndsWith(".DataContext", StringComparison.Ordinal))?
                .Elements().FirstOrDefault()?.Name;
            var current = typeName is null ? null : ResolveXamlType(typeName, compilation);
            foreach (var part in path.Split('.'))
            {
                var property = current?.GetMembers(part).OfType<IPropertySymbol>().FirstOrDefault();
                if (property is null)
                {
                    MarkUncertainMembers(part, compilation);
                    break;
                }

                Protect(property);
                current = property.Type as INamedTypeSymbol;
            }
        }
    }

    private void CollectRazor(MarkupDocumentSnapshot markup, Compilation compilation, IReadOnlyList<Compilation> compilations)
    {
        var componentName = Path.GetFileNameWithoutExtension(markup.FilePath);
        var owners = FindSourceTypes(compilation).Where(type => StringComparer.Ordinal.Equals(type.Name, componentName)).ToArray();
        var isPage = Regex.IsMatch(markup.Text, @"@page\s+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (isPage)
        {
            if (owners.Length == 1) Protect(owners[0]);
            else foreach (var owner in owners) MarkUncertain(owner);
        }

        if (owners.Length == 1)
        {
            foreach (Match handler in Regex.Matches(markup.Text, "@(?:on\\w+)\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                BindRazorMember(owners[0], handler.Groups[1].Value);
            }
        }

        foreach (Match tag in Regex.Matches(markup.Text, @"<([A-Z][\w.]*)\b([^>]*)>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            var name = tag.Groups[1].Value.Split('.').Last();
            var matches = compilations.SelectMany(FindSourceTypes).Where(type => StringComparer.Ordinal.Equals(type.Name, name)).ToArray();
            if (matches.Length == 1) Protect(matches[0]);
            else foreach (var match in matches) MarkTypeAndMethodsUncertain(match);
            foreach (Match attribute in Regex.Matches(tag.Groups[2].Value, @"(?:@bind-)?([\w-]+)\s*=", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                foreach (var type in matches)
                {
                    var properties = type.GetMembers(attribute.Groups[1].Value).OfType<IPropertySymbol>().ToArray();
                    if (properties.Length == 1) Protect(properties[0]);
                    else if (properties.Length > 1) foreach (var property in properties) MarkUncertain(property);
                }
            }

            foreach (Match handler in Regex.Matches(tag.Groups[2].Value, "@(?:on\\w+)\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                foreach (var component in matches) BindRazorMember(component, handler.Groups[1].Value);
            }
        }
    }

    private void BindRazorMember(INamedTypeSymbol owner, string expression)
    {
        var nameMatch = Regex.Match(expression, @"^@?(\w+)\s*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!nameMatch.Success)
        {
            foreach (var method in owner.GetMembers().OfType<IMethodSymbol>()) MarkUncertain(method);
            return;
        }

        var name = nameMatch.Groups[1].Value;
        var members = owner.GetMembers(name).Where(static member => member is IMethodSymbol or IPropertySymbol).ToArray();
        if (members.Length == 1) Protect(members[0]);
        else if (members.Length > 1) foreach (var member in members) MarkUncertain(member);
        else MarkUncertainMethods(name, owner.ContainingAssembly.GlobalNamespace);
    }

    private void CollectJavaScript(MarkupDocumentSnapshot markup, IReadOnlyList<Compilation> compilations)
    {
        var calls = Regex.Matches(markup.Text,
            """DotNet\.invokeMethod(?:Async)?\(\s*['"]([^'"]+)['"]\s*,\s*['"]([^'"]+)['"]""",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        foreach (Match call in calls)
        {
            var matchingCompilations = compilations.Where(compilation =>
                StringComparer.Ordinal.Equals(call.Groups[1].Value, compilation.AssemblyName)).ToArray();
            if (matchingCompilations.Length == 0) continue;
            if (matchingCompilations.Length > 1)
            {
                foreach (var candidateCompilation in matchingCompilations)
                {
                    MarkUncertainMethods(call.Groups[2].Value, candidateCompilation);
                }

                continue;
            }

            var methods = FindSourceTypes(matchingCompilations[0])
                .SelectMany(static type => type.GetMembers().OfType<IMethodSymbol>())
                .Where(method => method.GetAttributes().Any(attribute =>
                    IsFrameworkAttribute(attribute.AttributeClass, "Microsoft.JSInterop.JSInvokableAttribute", "Microsoft.JSInterop")
                    && StringComparer.Ordinal.Equals(attribute.ConstructorArguments.FirstOrDefault().Value as string ?? method.Name, call.Groups[2].Value)))
                .ToArray();
            if (methods.Length == 1) Protect(methods[0]);
            else if (methods.Length == 0) MarkUncertainMethods(call.Groups[2].Value, matchingCompilations[0]);
            else foreach (var method in methods) MarkUncertain(method);
        }
    }

    private static INamedTypeSymbol? ResolveXamlType(XName name, Compilation compilation) => ResolveXamlType(name.NamespaceName, name.LocalName, compilation);

    private static INamedTypeSymbol? ResolveXamlType(string xmlNamespace, string localName, Compilation compilation)
    {
        const string clrPrefix = "clr-namespace:";
        if (!xmlNamespace.StartsWith(clrPrefix, StringComparison.Ordinal)) return null;
        var separator = xmlNamespace.IndexOf(';');
        var clrNamespace = separator < 0 ? xmlNamespace[clrPrefix.Length..] : xmlNamespace[clrPrefix.Length..separator];
        return compilation.GetTypeByMetadataName(clrNamespace.Length == 0 ? localName : clrNamespace + "." + localName);
    }

    private void Protect(ISymbol symbol)
    {
        symbol = Normalize(symbol);
        protectedSymbols.Add(symbol);
        if (symbol.ContainingType is { } containingType)
        {
            protectedSymbols.Add(Normalize(containingType));
        }
    }

    private void MarkUncertain(ISymbol symbol) => uncertainSymbols.Add(Normalize(symbol));

    private void MarkTypeAndMethodsUncertain(INamedTypeSymbol type)
    {
        MarkUncertain(type);
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>()) MarkUncertain(method);
    }

    private static ISymbol Normalize(ISymbol symbol) => symbol is IMethodSymbol { ReducedFrom: { } reduced }
        ? reduced.OriginalDefinition
        : symbol.OriginalDefinition;

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

    private static bool IsFrameworkAttribute(INamedTypeSymbol? attributeType, string metadataName, string assemblyPrefix) =>
        attributeType is not null
        && StringComparer.Ordinal.Equals(attributeType.ToDisplayString(), metadataName)
        && attributeType.ContainingAssembly.Name.StartsWith(assemblyPrefix, StringComparison.Ordinal)
        && !IsSourceSymbol(attributeType);

    private void MarkTypesUncertain(string name, Compilation compilation)
    {
        foreach (var type in FindSourceTypes(compilation).Where(type => StringComparer.Ordinal.Equals(type.Name, name))) MarkUncertain(type);
    }

    private void MarkUncertainMethods(string name, Compilation compilation)
    {
        foreach (var method in FindSourceTypes(compilation).SelectMany(static type => type.GetMembers().OfType<IMethodSymbol>())
                     .Where(method => StringComparer.Ordinal.Equals(method.Name, name))) MarkUncertain(method);
    }

    private void MarkUncertainMembers(string name, Compilation compilation)
    {
        foreach (var member in FindSourceTypes(compilation).SelectMany(type => type.GetMembers(name))) MarkUncertain(member);
    }

    private void MarkUncertainMethods(string name, INamespaceSymbol root)
    {
        foreach (var method in SourceTypes(root).SelectMany(static type => type.GetMembers().OfType<IMethodSymbol>())
                     .Where(method => StringComparer.Ordinal.Equals(method.Name, name))) MarkUncertain(method);
    }

    private static IEnumerable<INamedTypeSymbol> FindSourceTypes(Compilation compilation) => SourceTypes(compilation.Assembly.GlobalNamespace);

    private static IEnumerable<INamedTypeSymbol> SourceTypes(INamespaceSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
            {
                foreach (var nested in SourceTypes(childNamespace)) yield return nested;
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var nested in NestedTypes(type)) yield return nested;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> NestedTypes(INamedTypeSymbol parent)
    {
        foreach (var nested in parent.GetTypeMembers())
        {
            yield return nested;
            foreach (var descendant in NestedTypes(nested)) yield return descendant;
        }
    }

    private static bool IsSourceSymbol(ISymbol symbol) => symbol.Locations.Any(static location => location.IsInSource);

    private static Project ResolveMarkupProject(IReadOnlyList<Project> projects, string path, string projectRoot)
    {
        var fullPath = Path.GetFullPath(path);
        var candidate = projects
            .Where(project => !string.IsNullOrWhiteSpace(project.FilePath))
            .Select(project => (Project: project, Directory: Path.GetDirectoryName(Path.GetFullPath(project.FilePath!))!))
            .Where(candidate => IsWithin(fullPath, candidate.Directory))
            .OrderByDescending(static candidate => candidate.Directory.Length)
            .FirstOrDefault();
        if (candidate.Project is null)
        {
            throw new AnalysisFailedException($"Markup snapshot has no owning C# project: '{path}' (root '{projectRoot}').");
        }

        return candidate.Project;
    }

    private static bool IsWithin(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
