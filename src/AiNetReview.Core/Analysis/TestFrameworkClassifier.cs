namespace AiNetReview.Core.Analysis;

using System;
using System.Linq;
using Microsoft.CodeAnalysis;

/// <summary>Classifies active test method roots from framework metadata.</summary>
internal static class TestFrameworkClassifier
{
    private const string XunitFactAttribute = "Xunit.FactAttribute";
    private const string XunitTheoryAttribute = "Xunit.TheoryAttribute";
    private const string XunitV3FactInterface = "Xunit.v3.IFactAttribute";
    private const string NUnitTestAttribute = "NUnit.Framework.TestAttribute";
    private const string NUnitTestCaseAttribute = "NUnit.Framework.TestCaseAttribute";
    private const string NUnitTestCaseSourceAttribute = "NUnit.Framework.TestCaseSourceAttribute";
    private const string NUnitTestFixtureAttribute = "NUnit.Framework.TestFixtureAttribute";
    private const string NUnitIgnoreAttribute = "NUnit.Framework.IgnoreAttribute";
    private const string NUnitExplicitAttribute = "NUnit.Framework.ExplicitAttribute";
    private const string MsTestMethodAttribute = "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute";
    private const string MsTestDataMethodAttribute = "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute";
    private const string MsTestClassAttribute = "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute";
    private const string MsTestIgnoreAttribute = "Microsoft.VisualStudio.TestTools.UnitTesting.IgnoreAttribute";

    public static bool IsActiveTestRoot(Project project, IMethodSymbol method)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(method);

        if (!ReviewSourceClassifier.IsTestProject(project) || method.ContainingType is null)
        {
            return false;
        }

        if (IsWholeMethodOrFixtureExcluded(method))
        {
            return false;
        }

        var methodAttributes = method.GetAttributes();
        if (methodAttributes.Any(static attribute => IsActiveXunitAttribute(attribute) && !IsExcludedXunitAttribute(attribute))
            || methodAttributes.Any(IsNUnitTestAttribute))
        {
            return true;
        }

        return HasAttribute(method.ContainingType, MsTestClassAttribute)
            && methodAttributes.Any(IsMSTestMethodAttribute);
    }

    private static bool IsWholeMethodOrFixtureExcluded(IMethodSymbol method)
    {
        if (HasAttribute(method, NUnitIgnoreAttribute)
            || HasAttribute(method, NUnitExplicitAttribute)
            || HasAttribute(method, MsTestIgnoreAttribute))
        {
            return true;
        }

        for (var type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (HasAttribute(type, NUnitIgnoreAttribute)
                || HasAttribute(type, NUnitExplicitAttribute)
                || HasAttribute(type, MsTestIgnoreAttribute)
                || type.GetAttributes().Any(IsExcludedNUnitFixtureAttribute))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsActiveXunitAttribute(AttributeData attribute)
    {
        var attributeClass = attribute.AttributeClass;
        return DerivesFromMetadataType(attributeClass, XunitFactAttribute)
            || DerivesFromMetadataType(attributeClass, XunitTheoryAttribute)
            || ImplementsMetadataInterface(attributeClass, XunitV3FactInterface);
    }

    private static bool IsExcludedXunitAttribute(AttributeData attribute)
    {
        if (!IsActiveXunitAttribute(attribute))
        {
            return false;
        }

        if (HasBooleanValue(attribute, "Explicit", true))
        {
            return true;
        }

        if (HasNonNullStringValue(attribute, "SkipWhen") || HasNonNullStringValue(attribute, "SkipUnless"))
        {
            return false;
        }

        return HasNonNullStringValue(attribute, "Skip");
    }

    private static bool IsNUnitTestAttribute(AttributeData attribute) =>
        DerivesFromMetadataType(attribute.AttributeClass, NUnitTestAttribute)
        || DerivesFromMetadataType(attribute.AttributeClass, NUnitTestCaseAttribute)
        || DerivesFromMetadataType(attribute.AttributeClass, NUnitTestCaseSourceAttribute);

    private static bool IsMSTestMethodAttribute(AttributeData attribute) =>
        DerivesFromMetadataType(attribute.AttributeClass, MsTestMethodAttribute)
        || DerivesFromMetadataType(attribute.AttributeClass, MsTestDataMethodAttribute);

    private static bool IsExcludedNUnitFixtureAttribute(AttributeData attribute)
    {
        if (!DerivesFromMetadataType(attribute.AttributeClass, NUnitTestFixtureAttribute))
        {
            return false;
        }

        return HasNonEmptyStringValue(attribute, "Ignore") || HasBooleanValue(attribute, "Explicit", true);
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().Any(attribute => DerivesFromMetadataType(attribute.AttributeClass, metadataName));

    private static bool DerivesFromMetadataType(INamedTypeSymbol? type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (IsMetadataType(current) && GetMetadataName(current) == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsMetadataInterface(INamedTypeSymbol? type, string metadataName) =>
        type is not null && type.AllInterfaces.Any(
            implemented => IsMetadataType(implemented) && GetMetadataName(implemented) == metadataName);

    private static bool IsMetadataType(INamedTypeSymbol type) => type.Locations.Any(static location => location.IsInMetadata);

    private static string GetMetadataName(INamedTypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    private static bool HasNonNullStringValue(AttributeData attribute, string name) =>
        TryGetValue(attribute, name, out var value)
        && value.Kind == TypedConstantKind.Primitive
        && value.Value is string;

    private static bool HasNonEmptyStringValue(AttributeData attribute, string name) =>
        TryGetValue(attribute, name, out var value)
        && value.Kind == TypedConstantKind.Primitive
        && value.Value is string text
        && text.Length > 0;

    private static bool HasBooleanValue(AttributeData attribute, string name, bool expected) =>
        TryGetValue(attribute, name, out var value)
        && value.Kind == TypedConstantKind.Primitive
        && value.Value is bool actual
        && actual == expected;

    private static bool TryGetValue(AttributeData attribute, string name, out TypedConstant value)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name)
            {
                value = argument.Value;
                return true;
            }
        }

        var parameters = attribute.AttributeConstructor?.Parameters;
        var arguments = attribute.ConstructorArguments;
        if (parameters is not null)
        {
            for (var index = 0; index < Math.Min(parameters.Value.Length, arguments.Length); index++)
            {
                if (string.Equals(parameters.Value[index].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = arguments[index];
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
