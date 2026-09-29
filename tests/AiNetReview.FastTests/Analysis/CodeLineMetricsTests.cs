namespace AiNetReview.FastTests.Analysis;

using System;
using System.Linq;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed class CodeLineMetricsTests
{
    [Fact]
    public void CountTokenStartLines_IgnoresTriviaAndCountsBlockBraces()
    {
        const string source = """
            #define FEATURE
            /// <summary>Documentation only.</summary>
            class C
            {
                void M()
                {
                    // comment

                    int first = 1; int second = 2;
                }
            }
            """;
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var block = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;

        Assert.Equal(3, CodeLineMetrics.CountTokenStartLines(block));
        Assert.Equal(7, CodeLineMetrics.CountTokenStartLines(root));
    }

    [Fact]
    public void CountTokenStartLines_CountsOnlyTheStartLineOfMultilineLiteral()
    {
        const string source = """"
            class C
            {
                void M()
                {
                    var text = """
                        first
                        second
                        """;
                }
            }
            """";
        var method = Parse(source).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

        Assert.Equal(5, CodeLineMetrics.CountTokenStartLines(method));
    }

    [Fact]
    public void CountTokenStartLines_IgnoresMissingTokensAndEndOfFile()
    {
        var rootWithMissingCloseAndEofLine = Parse("class C\n{\n");
        var rootWithMissingParameterTokens = Parse("class C\n{\n    void M(");

        Assert.Equal(2, CodeLineMetrics.CountTokenStartLines(rootWithMissingCloseAndEofLine));
        Assert.Equal(3, CodeLineMetrics.CountTokenStartLines(rootWithMissingParameterTokens));
    }

    [Fact]
    public void CountExecutableDeclaration_CountsWholeImplementedDeclarationAndExpressionBody()
    {
        const string source = """
            class C
            {
                [System.Obsolete]
                int M
                    (int value)
                {
                    return value;
                }

                int P => 1;
            }
            """;
        var root = Parse(source);
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var property = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();

        Assert.Equal(6, CodeLineMetrics.CountExecutableDeclaration(method));
        Assert.Equal(1, CodeLineMetrics.CountExecutableDeclaration(property));
    }

    [Fact]
    public void CountExecutableDeclaration_ReturnsZeroForBodylessAndDeclarationOnlyPartialMethod()
    {
        const string source = """
            abstract class AbstractType
            {
                public abstract void M();
                public abstract int P { get; set; }
            }

            partial class PartialType
            {
                partial void Local();
                partial void Local() { }
            }
            """;
        var root = Parse(source);
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var property = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();

        Assert.Equal(0, CodeLineMetrics.CountExecutableDeclaration(methods[0]));
        Assert.Equal(0, CodeLineMetrics.CountExecutableDeclaration(methods[1]));
        Assert.Equal(1, CodeLineMetrics.CountExecutableDeclaration(methods[2]));
        Assert.Equal(0, CodeLineMetrics.CountExecutableDeclaration(property));
    }

    [Fact]
    public void CountExecutableDeclaration_MeasuresPropertyAccessorsOnlyWhenPassedDirectly()
    {
        const string source = """
            class C
            {
                int P
                {
                    get
                    {
                        return 1;
                    }
                    set;
                }
            }
            """;
        var property = Parse(source).DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();

        Assert.Equal(0, CodeLineMetrics.CountExecutableDeclaration(property));
        Assert.Equal(4, CodeLineMetrics.CountExecutableDeclaration(property.AccessorList!.Accessors[0]));
        Assert.Equal(0, CodeLineMetrics.CountExecutableDeclaration(property.AccessorList.Accessors[1]));
    }

    [Fact]
    public void CountExecutableDeclaration_SupportsConstructorsOperatorsConversionsAndIndexers()
    {
        const string source = """
            class C
            {
                C() { }
                public static C operator +(C left, C right) => left;
                public static implicit operator int(C value) => 1;
                public int this[int index] { get => index; set { } }
            }
            """;
        var root = Parse(source);
        var constructor = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Single();
        var op = root.DescendantNodes().OfType<OperatorDeclarationSyntax>().Single();
        var conversion = root.DescendantNodes().OfType<ConversionOperatorDeclarationSyntax>().Single();
        var indexer = root.DescendantNodes().OfType<IndexerDeclarationSyntax>().Single();

        Assert.Equal(1, CodeLineMetrics.CountExecutableDeclaration(constructor));
        Assert.Equal(1, CodeLineMetrics.CountExecutableDeclaration(op));
        Assert.Equal(1, CodeLineMetrics.CountExecutableDeclaration(conversion));
        Assert.Equal(0, CodeLineMetrics.CountExecutableDeclaration(indexer));
        Assert.Equal(1, CodeLineMetrics.CountExecutableDeclaration(indexer.AccessorList!.Accessors[0]));
        Assert.Equal(1, CodeLineMetrics.CountExecutableDeclaration(indexer.AccessorList.Accessors[1]));
    }

    [Fact]
    public void CountExecutableDeclaration_IncludesLocalFunctionsAndLambdas()
    {
        const string source = """
            class C
            {
                void M()
                {
                    void Local()
                    {
                        int value = 1;
                    }
                    System.Func<int> callback = () =>
                    {
                        return 1;
                    };
                }
            }
            """;
        var method = Parse(source).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();

        Assert.Equal(11, CodeLineMetrics.CountExecutableDeclaration(method));
    }

    [Fact]
    public void CountExecutableDeclaration_RejectsUnsupportedDeclarationsAndNull()
    {
        var type = Parse("class C { }").DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        Assert.Throws<ArgumentNullException>(() => CodeLineMetrics.CountExecutableDeclaration(null!));
        Assert.Throws<ArgumentException>(() => CodeLineMetrics.CountExecutableDeclaration(type));
        Assert.Throws<ArgumentNullException>(() => CodeLineMetrics.CountTokenStartLines(null!));
        Assert.Throws<ArgumentNullException>(() => CodeLineMetrics.CountOwnTypePart(null!));
    }

    [Fact]
    public void CountOwnTypePart_ExcludesNestedTypesAndDelegates()
    {
        const string source = """
            class Outer
            {
                void M()
                {
                }
                class Inner
                {
                    void N() { }
                }
                delegate void Callback();
            }
            """;
        var root = Parse(source);
        var types = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToArray();
        var inner = types[1];

        Assert.Equal(6, CodeLineMetrics.CountOwnTypePart(types[0]));
        Assert.Equal(4, CodeLineMetrics.CountOwnTypePart(inner));
    }

    [Fact]
    public void CountOwnTypePart_AllowsNestedTypeTokensOnSamePhysicalLine()
    {
        var outer = Parse("class Outer { class Inner { } }").DescendantNodes().OfType<ClassDeclarationSyntax>().First();

        Assert.Equal(1, CodeLineMetrics.CountOwnTypePart(outer));
    }

    private static SyntaxNode Parse(string source) => CSharpSyntaxTree.ParseText(source).GetRoot();
}
