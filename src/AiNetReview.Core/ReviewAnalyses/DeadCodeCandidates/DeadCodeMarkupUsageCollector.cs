namespace AiNetReview.Core.ReviewAnalyses.DeadCodeCandidates;

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

/// <summary>Evaluates XAML, Razor, and JavaScript bindings in the loaded markup snapshot.</summary>
internal sealed class DeadCodeMarkupUsageCollector
{
    private readonly HashSet<ISymbol> protectedSymbols = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> uncertainSymbols = new(SymbolEqualityComparer.Default);

    private DeadCodeMarkupUsageCollector()
    {
    }

    public static async Task<DeadCodeMarkupUsage> CollectAsync(
        ReviewContext context,
        IReadOnlyList<Project> projects,
        IReadOnlyList<Compilation> compilations,
        CancellationToken cancellationToken)
    {
        var collector = new DeadCodeMarkupUsageCollector();
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
                        collector.CollectXaml(markup, compilation);
                        break;
                    case ".razor":
                        collector.CollectRazor(markup, compilation, compilations);
                        break;
                    case ".js":
                        collector.CollectJavaScript(markup, compilations);
                        break;
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                throw new AnalysisFailedException($"Markup could not be evaluated safely: '{markup.FilePath}'.", ex);
            }
        }

        return new DeadCodeMarkupUsage(collector.protectedSymbols, collector.uncertainSymbols);
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
        var owners = DeadCodeSourceTypeEnumerator.FindSourceTypes(compilation).Where(type => StringComparer.Ordinal.Equals(type.Name, componentName)).ToArray();
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
            var matches = compilations.SelectMany(DeadCodeSourceTypeEnumerator.FindSourceTypes).Where(type => StringComparer.Ordinal.Equals(type.Name, name)).ToArray();
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

            var methods = DeadCodeSourceTypeEnumerator.FindSourceTypes(matchingCompilations[0])
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
        symbol = DeadCodeSymbolNormalizer.Normalize(symbol);
        protectedSymbols.Add(symbol);
        if (symbol.ContainingType is { } containingType)
        {
            protectedSymbols.Add(DeadCodeSymbolNormalizer.Normalize(containingType));
        }
    }

    private void MarkUncertain(ISymbol symbol) => uncertainSymbols.Add(DeadCodeSymbolNormalizer.Normalize(symbol));

    private void MarkTypeAndMethodsUncertain(INamedTypeSymbol type)
    {
        MarkUncertain(type);
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>()) MarkUncertain(method);
    }

    private static bool IsFrameworkAttribute(INamedTypeSymbol? attributeType, string metadataName, string assemblyPrefix) =>
        attributeType is not null
        && StringComparer.Ordinal.Equals(attributeType.ToDisplayString(), metadataName)
        && attributeType.ContainingAssembly.Name.StartsWith(assemblyPrefix, StringComparison.Ordinal)
        && !attributeType.Locations.Any(static location => location.IsInSource);

    private void MarkTypesUncertain(string name, Compilation compilation)
    {
        foreach (var type in DeadCodeSourceTypeEnumerator.FindSourceTypes(compilation).Where(type => StringComparer.Ordinal.Equals(type.Name, name))) MarkUncertain(type);
    }

    private void MarkUncertainMethods(string name, Compilation compilation)
    {
        foreach (var method in DeadCodeSourceTypeEnumerator.FindSourceTypes(compilation).SelectMany(static type => type.GetMembers().OfType<IMethodSymbol>())
                     .Where(method => StringComparer.Ordinal.Equals(method.Name, name))) MarkUncertain(method);
    }

    private void MarkUncertainMembers(string name, Compilation compilation)
    {
        foreach (var member in DeadCodeSourceTypeEnumerator.FindSourceTypes(compilation).SelectMany(type => type.GetMembers(name))) MarkUncertain(member);
    }

    private void MarkUncertainMethods(string name, INamespaceSymbol root)
    {
        foreach (var method in DeadCodeSourceTypeEnumerator.SourceTypes(root).SelectMany(static type => type.GetMembers().OfType<IMethodSymbol>())
                     .Where(method => StringComparer.Ordinal.Equals(method.Name, name))) MarkUncertain(method);
    }

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

internal sealed record DeadCodeMarkupUsage(
    IReadOnlyCollection<ISymbol> ProtectedSymbols,
    IReadOnlyCollection<ISymbol> UncertainSymbols);
