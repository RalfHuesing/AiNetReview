namespace AiNetReview.Core.ReviewAnalyses.DeadCodeCandidates;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetReview.Core.Analysis;
using AiNetReview.Core.ReviewAnalyses;
using Microsoft.CodeAnalysis;

/// <summary>Protects test-runner entry points and statically identifiable data bindings.</summary>
internal static class DeadCodeTestFrameworkUsageCollector
{
    private const string XunitFact = "Xunit.FactAttribute";
    private const string XunitTheory = "Xunit.TheoryAttribute";
    private const string XunitFactInterface = "Xunit.v3.IFactAttribute";
    private const string NUnitRoot = "NUnit.Framework.";
    private const string MsTestRoot = "Microsoft.VisualStudio.TestTools.UnitTesting.";

    private enum ProviderFramework
    {
        Xunit,
        NUnit,
        MSTest,
    }

    public static async Task CollectAsync(
        Project project,
        Compilation compilation,
        Action<ISymbol> protect,
        Action<ISymbol> markUncertain,
        Action<string, Project> reportBroadExclusion,
        CancellationToken cancellationToken)
    {
        ProtectAssemblyBindings(compilation, protect, markUncertain, reportBroadExclusion, project);
        var types = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var document in project.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AnalysisFailedException($"Syntax could not be read for document '{document.Name}'.");
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new AnalysisFailedException($"Semantic model could not be created for document '{document.Name}'.");
            foreach (var declaration in root.DescendantNodes().Where(static node =>
                         node is Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax
                             or Microsoft.CodeAnalysis.CSharp.Syntax.DelegateDeclarationSyntax))
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol type)
                {
                    types.Add(type);
                    ProtectTypeBindings(type, compilation, protect, markUncertain, reportBroadExclusion, project);
                }
            }

            foreach (var declaration in root.DescendantNodes().Where(static node =>
                         node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax
                             or Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax))
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method)
                {
                    ProtectMethodBindings(method, compilation, protect, markUncertain, reportBroadExclusion, project);
                }
            }
        }

        foreach (var type in types)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProtectInheritedHooks(type, compilation, protect);
        }
    }

    private static void ProtectMethodBindings(
        IMethodSymbol method,
        Compilation compilation,
        Action<ISymbol> protect,
        Action<ISymbol> markUncertain,
        Action<string, Project> reportBroadExclusion,
        Project project)
    {
        var isTest = false;
        foreach (var attribute in method.GetAttributes())
        {
            var name = GetFrameworkAttributeName(attribute.AttributeClass, compilation);
            var xunitV3Fact = Implements(attribute.AttributeClass, compilation, XunitFactInterface);
            if (name is null)
            {
                if (xunitV3Fact)
                {
                    isTest = true;
                    protect(method);
                }

                continue;
            }

            var xunit = IsAttribute(attribute.AttributeClass, compilation, XunitFact)
                || IsAttribute(attribute.AttributeClass, compilation, XunitTheory)
                || xunitV3Fact;
            var nunit = name.StartsWith(NUnitRoot, StringComparison.Ordinal)
                && name is "NUnit.Framework.TestAttribute" or "NUnit.Framework.TestCaseAttribute"
                    or "NUnit.Framework.TestCaseSourceAttribute" or "NUnit.Framework.TheoryAttribute";
            var mstest = name is "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute"
                or "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute";
            if (xunit || nunit || mstest)
            {
                isTest = true;
                protect(method);
            }

            if (IsAttribute(attribute.AttributeClass, compilation, "Xunit.MemberDataAttribute"))
            {
                BindDataAttribute(attribute, method.ContainingType, "MemberType", 0, compilation,
                    ProviderFramework.Xunit, protect, markUncertain, reportBroadExclusion, project);
            }
            else if (IsAttribute(attribute.AttributeClass, compilation, "Xunit.ClassDataAttribute"))
            {
                BindTypeArguments(attribute, compilation, protect, markUncertain, reportBroadExclusion, project);
            }
            else if (IsNUnitDataAttribute(name))
            {
                BindDataAttribute(attribute, method.ContainingType, "SourceType", 1, compilation,
                    ProviderFramework.NUnit, protect, markUncertain, reportBroadExclusion, project);
            }
            else if (IsAttribute(attribute.AttributeClass, compilation, "Microsoft.VisualStudio.TestTools.UnitTesting.DynamicDataAttribute"))
            {
                BindDataAttribute(attribute, method.ContainingType, "DynamicDataSourceType", 1, compilation,
                    ProviderFramework.MSTest, protect, markUncertain, reportBroadExclusion, project);
                BindMSTestDisplayNameCallback(attribute, method.ContainingType, protect, markUncertain);
            }
        }

        foreach (var parameter in method.Parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                if (IsAttribute(attribute.AttributeClass, compilation, "NUnit.Framework.ValueSourceAttribute"))
                {
                    BindDataAttribute(attribute, method.ContainingType, "SourceType", 1, compilation,
                        ProviderFramework.NUnit, protect, markUncertain, reportBroadExclusion, project);
                }
            }
        }

        if (isTest)
        {
            ProtectContainingTypes(method.ContainingType, protect);
        }
    }

    private static void ProtectAssemblyBindings(
        Compilation compilation,
        Action<ISymbol> protect,
        Action<ISymbol> markUncertain,
        Action<string, Project> reportBroadExclusion,
        Project project)
    {
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            var name = GetFrameworkAttributeName(attribute.AttributeClass, compilation);
            if (name is "Xunit.AssemblyFixtureAttribute" or "Xunit.v3.AssemblyFixtureAttribute"
                or "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyFixtureProviderAttribute")
            {
                BindTypeArguments(attribute, compilation, protect, markUncertain, reportBroadExclusion, project);
                foreach (var type in attribute.ConstructorArguments.SelectMany(Flatten).OfType<INamedTypeSymbol>())
                {
                    ProtectAttributedLifecycleMembers(type, compilation, protect);
                }
            }
        }
    }

    private static void ProtectTypeBindings(
        INamedTypeSymbol type,
        Compilation compilation,
        Action<ISymbol> protect,
        Action<ISymbol> markUncertain,
        Action<string, Project> reportBroadExclusion,
        Project project)
    {
        foreach (var attribute in type.GetAttributes())
        {
            var name = GetFrameworkAttributeName(attribute.AttributeClass, compilation);
            if (name is null)
            {
                continue;
            }

            var testFixture = name is "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute"
                or "NUnit.Framework.TestFixtureAttribute" or "NUnit.Framework.TestFixtureSourceAttribute"
                or "NUnit.Framework.SetUpFixtureAttribute"
                or "Xunit.CollectionAttribute" or "Xunit.CollectionDefinitionAttribute"
                or "Xunit.AssemblyFixtureAttribute" or "Xunit.v3.AssemblyFixtureAttribute";
            if (testFixture)
            {
                ProtectContainingTypes(type, protect);
            }

            if (name is "NUnit.Framework.TestFixtureSourceAttribute")
            {
                BindDataAttribute(attribute, type, "SourceType", 1, compilation,
                    ProviderFramework.NUnit, protect, markUncertain, reportBroadExclusion, project);
            }

            if (name is "Xunit.CollectionAttribute")
            {
                BindCollectionByName(attribute, compilation, protect, markUncertain, reportBroadExclusion, project);
            }

            if (name is "Xunit.CollectionDefinitionAttribute")
            {
                ProtectContainingTypes(type, protect);
                foreach (var fixtureContract in type.AllInterfaces.Where(iface =>
                             IsFrameworkInterface(iface, compilation, "Xunit.IClassFixture`1")
                             || IsFrameworkInterface(iface, compilation, "Xunit.ICollectionFixture`1")))
                {
                    foreach (var fixtureType in fixtureContract.TypeArguments.OfType<INamedTypeSymbol>())
                    {
                        ProtectProviderType(fixtureType, protect);
                    }
                }
            }

            if (name is "Xunit.AssemblyFixtureAttribute" or "Xunit.v3.AssemblyFixtureAttribute")
            {
                BindTypeArguments(attribute, compilation, protect, markUncertain, reportBroadExclusion, project);
            }

            if (name is "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyFixtureProviderAttribute")
            {
                BindTypeArguments(attribute, compilation, protect, markUncertain, reportBroadExclusion, project);
            }
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (IsFrameworkInterface(iface, compilation, "Xunit.IClassFixture`1")
                || IsFrameworkInterface(iface, compilation, "Xunit.ICollectionFixture`1"))
            {
                ProtectContainingTypes(type, protect);
                foreach (var argument in iface.TypeArguments.OfType<INamedTypeSymbol>())
                {
                    ProtectProviderType(argument, protect);
                }
            }

            if (IsFrameworkInterface(iface, compilation, "Xunit.IAsyncLifetime")
                || IsFrameworkInterface(iface, compilation, "System.IDisposable")
                || IsFrameworkInterface(iface, compilation, "System.IAsyncDisposable"))
            {
                ProtectFrameworkContractImplementations(type, iface, protect);
            }
        }

        foreach (var member in type.GetMembers())
        {
            foreach (var attribute in member.GetAttributes())
            {
                if (IsAttribute(attribute.AttributeClass, compilation, "NUnit.Framework.DatapointAttribute"))
                {
                    protect(member);
                }
                else if (IsAttribute(attribute.AttributeClass, compilation, "NUnit.Framework.DatapointSourceAttribute"))
                {
                    ProtectContainingTypes(type, protect);
                    protect(member);
                }
            }
        }
    }

    private static void ProtectInheritedHooks(INamedTypeSymbol type, Compilation compilation, Action<ISymbol> protect)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.GetAttributes().Any(attribute => IsLifecycleAttribute(attribute.AttributeClass, compilation)
                        || IsTestAttribute(attribute.AttributeClass, compilation)))
                {
                    protect(method);
                    ProtectContainingTypes(type, protect);
                    if (!SymbolEqualityComparer.Default.Equals(method.ContainingType, type) && type.GetMembers().OfType<IMethodSymbol>().Any(candidate =>
                            SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod, method)))
                    {
                        protect(type.GetMembers().OfType<IMethodSymbol>().First(candidate =>
                            SymbolEqualityComparer.Default.Equals(candidate.OverriddenMethod, method)));
                    }
                }
            }
        }
    }

    private static bool IsLifecycleAttribute(INamedTypeSymbol? attribute, Compilation compilation)
    {
        var name = GetFrameworkAttributeName(attribute, compilation);
        return name is "Xunit.BeforeAfterTestAttribute"
            or "NUnit.Framework.SetUpAttribute" or "NUnit.Framework.TearDownAttribute"
            or "NUnit.Framework.OneTimeSetUpAttribute" or "NUnit.Framework.OneTimeTearDownAttribute"
            or "NUnit.Framework.TestFixtureSetUpAttribute" or "NUnit.Framework.TestFixtureTearDownAttribute"
            or "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyInitializeAttribute"
            or "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyCleanupAttribute"
            or "Microsoft.VisualStudio.TestTools.UnitTesting.ClassInitializeAttribute"
            or "Microsoft.VisualStudio.TestTools.UnitTesting.ClassCleanupAttribute"
            or "Microsoft.VisualStudio.TestTools.UnitTesting.TestInitializeAttribute"
            or "Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanupAttribute"
            or "Microsoft.VisualStudio.TestTools.UnitTesting.GlobalTestInitializeAttribute"
            or "Microsoft.VisualStudio.TestTools.UnitTesting.GlobalTestCleanupAttribute";
    }

    private static bool IsTestAttribute(INamedTypeSymbol? attribute, Compilation compilation) =>
        IsAttribute(attribute, compilation, XunitFact)
        || IsAttribute(attribute, compilation, XunitTheory)
        || Implements(attribute, compilation, XunitFactInterface)
        || IsAttribute(attribute, compilation, "NUnit.Framework.TestAttribute")
        || IsAttribute(attribute, compilation, "NUnit.Framework.TestCaseAttribute")
        || IsAttribute(attribute, compilation, "NUnit.Framework.TestCaseSourceAttribute")
        || IsAttribute(attribute, compilation, "NUnit.Framework.TheoryAttribute")
        || IsAttribute(attribute, compilation, "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute")
        || IsAttribute(attribute, compilation, "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute");

    private static void BindDataAttribute(
        AttributeData attribute,
        INamedTypeSymbol defaultType,
        string sourceTypeProperty,
        int sourceTypeConstructorIndex,
        Compilation compilation,
        ProviderFramework framework,
        Action<ISymbol> protect,
        Action<ISymbol> markUncertain,
        Action<string, Project> reportBroadExclusion,
        Project project)
    {
        var asyncSupport = GetAsyncProviderSupport(attribute.AttributeClass, compilation, framework);
        var namedSourceType = attribute.NamedArguments.FirstOrDefault(pair => StringComparer.Ordinal.Equals(pair.Key, sourceTypeProperty));
        var hasNamedSourceType = attribute.NamedArguments.Any(pair => StringComparer.Ordinal.Equals(pair.Key, sourceTypeProperty));
        var typeArguments = attribute.ConstructorArguments.SelectMany(Flatten).OfType<INamedTypeSymbol>().ToArray();
        var sourceType = namedSourceType.Value.Value as INamedTypeSymbol
            ?? typeArguments.FirstOrDefault()
            ?? defaultType;
        var name = attribute.ConstructorArguments.FirstOrDefault(static argument => argument.Value is string).Value as string;
        if ((hasNamedSourceType && namedSourceType.Value.Value is null)
            || sourceType.TypeKind == TypeKind.Error)
        {
            MarkBroadlyUncertain(compilation, project, "Recognized framework data attribute has no statically resolvable source type.",
                protect, markUncertain, reportBroadExclusion);
            return;
        }

        var allMembers = GetMembersIncludingBase(sourceType).Where(static member => member is IMethodSymbol or IPropertySymbol or IFieldSymbol).ToArray();
        if (name is null)
        {
            ProtectProviderType(sourceType, protect);
            foreach (var member in allMembers)
            {
                if (IsPlausibleProviderMember(member, framework, asyncSupport))
                {
                    markUncertain(member);
                }
            }

            return;
        }

        var matching = allMembers.Where(member => StringComparer.Ordinal.Equals(member.Name, name)).ToArray();
        if (matching.Length == 0)
        {
            foreach (var member in allMembers)
            {
                if (IsPlausibleProviderMember(member, framework, asyncSupport))
                {
                    markUncertain(member);
                }
            }

            ProtectProviderType(sourceType, protect);
            return;
        }

        foreach (var member in matching)
        {
            protect(member);
            if (matching.Length > 1)
            {
                markUncertain(member);
            }
        }

        if (matching.Length > 1)
        {
            foreach (var member in allMembers.Where(member => IsPlausibleProviderMember(member, framework, asyncSupport)))
            {
                markUncertain(member);
            }
        }
    }

    private static void BindTypeArguments(
        AttributeData attribute,
        Compilation compilation,
        Action<ISymbol> protect,
        Action<ISymbol> markUncertain,
        Action<string, Project> reportBroadExclusion,
        Project project)
    {
        var types = attribute.ConstructorArguments.SelectMany(Flatten)
            .Concat(attribute.NamedArguments.SelectMany(static pair => Flatten(pair.Value)))
            .OfType<INamedTypeSymbol>().Where(static type => type.TypeKind != TypeKind.Error).ToArray();
        if (types.Length == 0)
        {
            MarkBroadlyUncertain(compilation, project, "Recognized framework data attribute has no statically resolvable provider type.",
                protect, markUncertain, reportBroadExclusion);
            return;
        }

        foreach (var type in types)
        {
            ProtectProviderType(type, protect);
        }
    }

    private static IEnumerable<object?> Flatten(TypedConstant constant)
    {
        if (constant.Kind == TypedConstantKind.Array)
        {
            return constant.Values.SelectMany(Flatten);
        }

        return [constant.Value];
    }

    private static void BindMSTestDisplayNameCallback(AttributeData attribute, INamedTypeSymbol declaringType, Action<ISymbol> protect, Action<ISymbol> markUncertain)
    {
        var name = attribute.NamedArguments.FirstOrDefault(static pair => pair.Key is "DynamicDataDisplayName" or "DynamicDataDisplayNameMethodName").Value.Value as string;
        if (name is null)
        {
            return;
        }

        var targetType = attribute.NamedArguments.FirstOrDefault(static pair => pair.Key == "DynamicDataDisplayNameDeclaringType").Value.Value as INamedTypeSymbol
            ?? declaringType;
        var matches = GetMembersIncludingBase(targetType).Where(member => member is IMethodSymbol && member.Name == name).ToArray();
        if (matches.Length == 0)
        {
            foreach (var member in GetMembersIncludingBase(targetType).OfType<IMethodSymbol>()
                         .Where(IsPlausibleMSTestDisplayNameCallback))
            {
                markUncertain(member);
            }

            return;
        }

        foreach (var member in matches)
        {
            protect(member);
        }
    }

    private static void ProtectAttributedLifecycleMembers(INamedTypeSymbol type, Compilation compilation, Action<ISymbol> protect)
    {
        ProtectContainingTypes(type, protect);
        foreach (var current in GetBaseTypes(type))
        {
            foreach (var member in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (member.GetAttributes().Any(attribute => IsLifecycleAttribute(attribute.AttributeClass, compilation)))
                {
                    protect(member);
                }
            }
        }
    }

    private static void BindCollectionByName(AttributeData attribute, Compilation compilation, Action<ISymbol> protect, Action<ISymbol> markUncertain, Action<string, Project> reportBroadExclusion, Project project)
    {
        var name = attribute.ConstructorArguments.FirstOrDefault().Value as string;
        if (name is null)
        {
            MarkBroadlyUncertain(compilation, project, "xUnit collection binding has no static collection name.", protect, markUncertain, reportBroadExclusion);
            return;
        }

        foreach (var type in SourceTypes(compilation.Assembly.GlobalNamespace))
        {
            if (type.GetAttributes().Any(candidate => IsAttribute(candidate.AttributeClass, compilation, "Xunit.CollectionDefinitionAttribute")
                    && StringComparer.Ordinal.Equals(candidate.ConstructorArguments.FirstOrDefault().Value as string, name)))
            {
                ProtectProviderType(type, protect);
            }
        }
    }

    private static void MarkBroadlyUncertain(
        Compilation compilation,
        Project project,
        string reason,
        Action<ISymbol> protect,
        Action<ISymbol> markUncertain,
        Action<string, Project> reportBroadExclusion)
    {
        foreach (var type in SourceTypes(compilation.Assembly.GlobalNamespace))
        {
            markUncertain(type);
            foreach (var member in type.GetMembers())
            {
                markUncertain(member);
            }
        }

        reportBroadExclusion(reason, project);
    }

    private static void ProtectProviderType(INamedTypeSymbol type, Action<ISymbol> protect)
    {
        ProtectContainingTypes(type, protect);
        foreach (var contract in type.AllInterfaces)
        {
            foreach (var contractMember in contract.GetMembers())
            {
                if (type.FindImplementationForInterfaceMember(contractMember) is { } implementation)
                {
                    protect(implementation);
                }
            }
        }
    }

    private static void ProtectFrameworkContractImplementations(INamedTypeSymbol type, INamedTypeSymbol contract, Action<ISymbol> protect)
    {
        ProtectContainingTypes(type, protect);
        foreach (var contractMember in contract.GetMembers())
        {
            if (type.FindImplementationForInterfaceMember(contractMember) is { } implementation)
            {
                protect(implementation);
            }
        }
    }

    private static void ProtectContainingTypes(INamedTypeSymbol? type, Action<ISymbol> protect)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            protect(current);
        }
    }

    private static IEnumerable<ISymbol> GetMembersIncludingBase(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                yield return member;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetBaseTypes(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static bool IsPlausibleProviderMember(ISymbol member, ProviderFramework framework, (bool Task, bool AsyncEnumerable) asyncSupport)
    {
        if (!member.IsStatic || (framework != ProviderFramework.NUnit && member.DeclaredAccessibility != Accessibility.Public))
        {
            return false;
        }

        ITypeSymbol? valueType = member switch
        {
            IMethodSymbol method => method.ReturnType,
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };
        if (valueType is null || (member is IMethodSymbol && valueType.SpecialType == SpecialType.System_Void))
        {
            return false;
        }

        return IsEnumerableProviderType(valueType, asyncSupport);
    }

    private static (bool Task, bool AsyncEnumerable) GetAsyncProviderSupport(
        INamedTypeSymbol? attributeType,
        Compilation compilation,
        ProviderFramework framework)
    {
        var metadataName = GetFrameworkAttributeName(attributeType, compilation);
        var frameworkAttribute = GetBaseTypes(attributeType ?? compilation.GetSpecialType(SpecialType.System_Object))
            .FirstOrDefault(type => metadataName is not null
                && StringComparer.Ordinal.Equals(GetMetadataName(type.OriginalDefinition), metadataName)
                && IsFrameworkMetadataType(type, metadataName));
        var assembly = frameworkAttribute?.ContainingAssembly;
        if (assembly is null)
        {
            return (false, false);
        }

        if (framework == ProviderFramework.Xunit
            && assembly.Name.StartsWith("xunit.v3.", StringComparison.OrdinalIgnoreCase))
        {
            return (true, true);
        }

        if (framework == ProviderFramework.NUnit)
        {
            var version = assembly.Identity.Version;
            return (version >= new Version(3, 14), version >= new Version(4, 0));
        }

        return (false, false);
    }

    private static bool IsPlausibleMSTestDisplayNameCallback(IMethodSymbol method) =>
        method.IsStatic && method.DeclaredAccessibility == Accessibility.Public
        && method.ReturnType.SpecialType == SpecialType.System_String;

    private static bool IsEnumerableProviderType(ITypeSymbol type, (bool Task, bool AsyncEnumerable) asyncSupport)
    {
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var metadataName = GetMetadataName(namedType.OriginalDefinition);
        if ((asyncSupport.Task && metadataName is "System.Threading.Tasks.Task`1" or "System.Threading.Tasks.ValueTask`1"))
        {
            return namedType.TypeArguments.Length == 1 && IsEnumerableProviderType(namedType.TypeArguments[0], asyncSupport);
        }

        if (namedType.SpecialType != SpecialType.System_String
            && (IsEnumerableInterface(namedType)
                || namedType.AllInterfaces.Any(IsEnumerableInterface)))
        {
            return true;
        }

        return asyncSupport.AsyncEnumerable && (GetMetadataName(namedType.OriginalDefinition) == "System.Collections.Generic.IAsyncEnumerable`1"
            || namedType.AllInterfaces.Any(iface =>
                GetMetadataName(iface.OriginalDefinition) == "System.Collections.Generic.IAsyncEnumerable`1"));
    }

    private static bool IsEnumerableInterface(INamedTypeSymbol type) =>
        type.SpecialType == SpecialType.System_Collections_IEnumerable
        || GetMetadataName(type.OriginalDefinition) == "System.Collections.Generic.IEnumerable`1";

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        var name = type.MetadataName;
        for (var containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            name = containing.MetadataName + "+" + name;
        }

        return type.ContainingNamespace.IsGlobalNamespace
            ? name
            : type.ContainingNamespace.ToDisplayString() + "." + name;
    }

    private static IEnumerable<INamedTypeSymbol> SourceTypes(INamespaceSymbol root)
    {
        foreach (var type in root.GetTypeMembers())
        {
            foreach (var nested in SourceTypes(type))
            {
                yield return nested;
            }
        }

        foreach (var child in root.GetNamespaceMembers())
        {
            foreach (var type in SourceTypes(child))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> SourceTypes(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var descendant in SourceTypes(nested))
            {
                yield return descendant;
            }
        }
    }

    private static INamedTypeSymbol? GetNamedType(AttributeData attribute, string property) =>
        attribute.NamedArguments.FirstOrDefault(pair => StringComparer.Ordinal.Equals(pair.Key, property)).Value.Value as INamedTypeSymbol;

    private static bool IsNUnitDataAttribute(string? name) => name is "NUnit.Framework.TestCaseSourceAttribute"
        or "NUnit.Framework.TestFixtureSourceAttribute" or "NUnit.Framework.ValueSourceAttribute"
        or "NUnit.Framework.DatapointAttribute" or "NUnit.Framework.DatapointSourceAttribute";

    private static bool Implements(INamedTypeSymbol? type, Compilation compilation, string metadataName) =>
        type is not null && type.AllInterfaces.Any(iface => IsFrameworkInterface(iface, compilation, metadataName));

    private static bool IsFrameworkInterface(INamedTypeSymbol actual, Compilation compilation, string metadataName) =>
        compilation.GetTypeByMetadataName(metadataName) is { } frameworkType
        && IsFrameworkMetadataType(frameworkType, metadataName)
        && SymbolEqualityComparer.Default.Equals(actual.OriginalDefinition, frameworkType.OriginalDefinition);

    private static bool IsAttribute(INamedTypeSymbol? actual, Compilation compilation, string metadataName)
    {
        var frameworkType = compilation.GetTypeByMetadataName(metadataName);
        if (frameworkType is null || !IsFrameworkMetadataType(frameworkType, metadataName))
        {
            return false;
        }

        for (var current = actual; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, frameworkType.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetFrameworkAttributeName(INamedTypeSymbol? type, Compilation compilation)
    {
        if (type is null)
        {
            return null;
        }

        var known = new[]
        {
            XunitFact, XunitTheory, "Xunit.MemberDataAttribute", "Xunit.ClassDataAttribute", "Xunit.CollectionAttribute",
            "Xunit.CollectionDefinitionAttribute", "Xunit.AssemblyFixtureAttribute", "Xunit.v3.AssemblyFixtureAttribute",
            "Xunit.BeforeAfterTestAttribute", "NUnit.Framework.TestAttribute", "NUnit.Framework.TestCaseAttribute",
            "NUnit.Framework.TestCaseSourceAttribute", "NUnit.Framework.TheoryAttribute", "NUnit.Framework.TestFixtureAttribute",
            "NUnit.Framework.TestFixtureSourceAttribute", "NUnit.Framework.SetUpFixtureAttribute", "NUnit.Framework.SetUpAttribute",
            "NUnit.Framework.TearDownAttribute", "NUnit.Framework.OneTimeSetUpAttribute", "NUnit.Framework.OneTimeTearDownAttribute",
            "NUnit.Framework.TestFixtureSetUpAttribute", "NUnit.Framework.TestFixtureTearDownAttribute", "NUnit.Framework.ValueSourceAttribute",
            "NUnit.Framework.DatapointAttribute", "NUnit.Framework.DatapointSourceAttribute",
            "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute", "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute",
            "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute", "Microsoft.VisualStudio.TestTools.UnitTesting.DynamicDataAttribute",
            "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyFixtureProviderAttribute",
            "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyInitializeAttribute", "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyCleanupAttribute",
            "Microsoft.VisualStudio.TestTools.UnitTesting.ClassInitializeAttribute", "Microsoft.VisualStudio.TestTools.UnitTesting.ClassCleanupAttribute",
            "Microsoft.VisualStudio.TestTools.UnitTesting.TestInitializeAttribute", "Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanupAttribute",
            "Microsoft.VisualStudio.TestTools.UnitTesting.GlobalTestInitializeAttribute", "Microsoft.VisualStudio.TestTools.UnitTesting.GlobalTestCleanupAttribute",
        };
        foreach (var metadataName in known)
        {
            if (IsAttribute(type, compilation, metadataName))
            {
                return metadataName;
            }
        }

        return null;
    }

    private static bool IsSourceSymbol(ISymbol symbol) => symbol.Locations.Any(static location => location.IsInSource);

    private static bool IsFrameworkMetadataType(INamedTypeSymbol type, string metadataName)
    {
        if (IsSourceSymbol(type))
        {
            return false;
        }

        var assemblyName = type.ContainingAssembly.Name;
        if (metadataName.StartsWith("Xunit.", StringComparison.Ordinal))
        {
            return assemblyName.StartsWith("xunit.", StringComparison.OrdinalIgnoreCase)
                || StringComparer.OrdinalIgnoreCase.Equals(assemblyName, "xunit.core");
        }

        if (metadataName.StartsWith("NUnit.Framework.", StringComparison.Ordinal))
        {
            return StringComparer.OrdinalIgnoreCase.Equals(assemblyName, "nunit.framework");
        }

        if (metadataName.StartsWith("Microsoft.VisualStudio.TestTools.UnitTesting.", StringComparison.Ordinal))
        {
            return StringComparer.OrdinalIgnoreCase.Equals(assemblyName, "MSTest.TestFramework")
                || assemblyName.StartsWith("Microsoft.VisualStudio.TestPlatform.TestFramework", StringComparison.OrdinalIgnoreCase);
        }

        return metadataName.StartsWith("System.", StringComparison.Ordinal);
    }
}
