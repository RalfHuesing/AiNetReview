namespace AiNetReview.FastTests.Analysis;

using System;
using System.Linq;
using AiNetReview.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed class ControlFlowMetricsTests
{
    [Theory]
    [InlineData("if (a) { }", 1, 1, 1, SyntaxKind.IfStatement)]
    [InlineData("if (a) { } else if (b) { } else if (c) { }", 3, 3, 1, SyntaxKind.IfStatement)]
    [InlineData("switch (value) { case 0: case 1: break; default: break; }", 2, 1, 1, SyntaxKind.SwitchSection)]
    [InlineData("switch (value) { }", 0, 1, 0, null)]
    [InlineData("var result = value switch { 0 => 0, 1 => 1, _ => 2 };", 3, 1, 1, SyntaxKind.SwitchExpressionArm)]
    [InlineData("var result = a ? 1 : 0;", 1, 1, 1, SyntaxKind.ConditionalExpression)]
    [InlineData("for (var i = 0; i < 1; i++) { }", 1, 1, 1, SyntaxKind.ForStatement)]
    [InlineData("foreach (var item in values) { }", 1, 1, 1, SyntaxKind.ForEachStatement)]
    [InlineData("foreach (var (key, item) in values) { }", 1, 1, 1, SyntaxKind.ForEachVariableStatement)]
    [InlineData("while (a) { }", 1, 1, 1, SyntaxKind.WhileStatement)]
    [InlineData("do { } while (a);", 1, 1, 1, SyntaxKind.DoStatement)]
    [InlineData("try { } catch (Exception) { }", 1, 1, 1, SyntaxKind.CatchClause)]
    public void Measure_CountsEachDecisionForm(string statements, int count, int constructs, int depth, SyntaxKind? deepestKind)
    {
        var measurement = ControlFlowMetrics.Measure(GetMethodBody(statements));

        Assert.Equal(count, measurement.DecisionCount);
        Assert.Equal(constructs, measurement.DecisionConstructCount);
        Assert.Equal(depth, measurement.MaxDecisionNesting);
        Assert.Equal(deepestKind, measurement.DeepestDecision?.Kind());
    }

    [Fact]
    public void Measure_HandlesEmptyBodyAndExpressionBody()
    {
        var empty = ControlFlowMetrics.Measure(GetMethodBody(string.Empty));
        Assert.Equal(new ControlFlowMeasurement(0, 0, 0, null), empty);

        var expression = GetMethodExpression("a ? (b ? (c ? 1 : 2) : 3) : 4");
        var measured = ControlFlowMetrics.Measure(expression);
        Assert.Equal(3, measured.DecisionCount);
        Assert.Equal(3, measured.DecisionConstructCount);
        Assert.Equal(3, measured.MaxDecisionNesting);
        Assert.Equal(SyntaxKind.ConditionalExpression, measured.DeepestDecision?.Kind());
    }

    [Fact]
    public void Measure_DoesNotCountBooleanOperatorsPatternsNullCoalescingOrExceptionFilters()
    {
        var body = GetMethodBody("var combined = a && b || c; var fallback = value ?? 0; value ??= 1; if (value is > 0 and < 4) { } try { } catch (Exception) when (a && b) { }");

        var measured = ControlFlowMetrics.Measure(body);

        Assert.Equal(2, measured.DecisionCount);
        Assert.Equal(2, measured.DecisionConstructCount);
        Assert.Equal(1, measured.MaxDecisionNesting);
        Assert.Equal(SyntaxKind.IfStatement, measured.DeepestDecision?.Kind());
    }

    [Fact]
    public void Measure_VisitsDecisionsInConditionsAndBranchesAtSuccessiveLevels()
    {
        var body = GetMethodBody("if (a ? b : c) { if (b) { } }");

        var measured = ControlFlowMetrics.Measure(body);

        Assert.Equal(3, measured.DecisionCount);
        Assert.Equal(3, measured.DecisionConstructCount);
        Assert.Equal(2, measured.MaxDecisionNesting);
        Assert.Equal(SyntaxKind.ConditionalExpression, measured.DeepestDecision?.Kind());
    }

    [Fact]
    public void Measure_VisitsDecisionsInsideSwitchExpressionArmsAtSuccessiveLevels()
    {
        var body = GetMethodBody("var result = value switch { 0 => a ? b : c, _ => false };");
        var conditional = body.DescendantNodes().OfType<ConditionalExpressionSyntax>().Single();

        var measured = ControlFlowMetrics.Measure(body);

        Assert.Equal(3, measured.DecisionCount);
        Assert.Equal(2, measured.DecisionConstructCount);
        Assert.Equal(2, measured.MaxDecisionNesting);
        Assert.Same(conditional, measured.DeepestDecision);
    }

    [Fact]
    public void Measure_VisitsSwitchGoverningExpressionAtCurrentDepthAndSectionsAsLevels()
    {
        var body = GetMethodBody("switch (a ? 1 : 2) { case 1: if (a) { } break; default: break; }");

        var measured = ControlFlowMetrics.Measure(body);

        Assert.Equal(4, measured.DecisionCount);
        Assert.Equal(3, measured.DecisionConstructCount);
        Assert.Equal(2, measured.MaxDecisionNesting);
        Assert.Equal(SyntaxKind.IfStatement, measured.DeepestDecision?.Kind());
    }

    [Fact]
    public void Measure_KeepsFirstDecisionWhenMaximumDepthIsTied()
    {
        const string source = "class C { void M(bool a, bool b, bool c, bool d) { if (a ? b : c) { } if (b ? c : d) { } } }";
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var firstConditional = root.DescendantNodes().OfType<ConditionalExpressionSyntax>().First();
        var body = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;

        var measured = ControlFlowMetrics.Measure(body);

        Assert.Equal(4, measured.DecisionCount);
        Assert.Equal(4, measured.DecisionConstructCount);
        Assert.Equal(2, measured.MaxDecisionNesting);
        Assert.Same(firstConditional, measured.DeepestDecision);
    }

    [Fact]
    public void Measure_SkipsLocalFunctionsLambdasAndAnonymousMethods()
    {
        var body = GetMethodBody("if (a) { } void Local() { if (a) { } } System.Func<bool> simple = () => a ? b : c; System.Func<bool> block = () => { if (a) { } return b; }; System.Func<bool> anonymous = delegate { if (a) { } return b; };");

        var measured = ControlFlowMetrics.Measure(body);

        Assert.Equal(1, measured.DecisionCount);
        Assert.Equal(1, measured.DecisionConstructCount);
        Assert.Equal(1, measured.MaxDecisionNesting);
        Assert.Equal(SyntaxKind.IfStatement, measured.DeepestDecision?.Kind());
    }

    [Fact]
    public void Measure_RejectsNullAndNonBodySyntax()
    {
        Assert.Throws<ArgumentNullException>(() => ControlFlowMetrics.Measure(null!));
        Assert.Throws<ArgumentException>(() => ControlFlowMetrics.Measure(CSharpSyntaxTree.ParseText("class C { }").GetRoot()));
    }

    private static BlockSyntax GetMethodBody(string statements)
    {
        var root = CSharpSyntaxTree.ParseText("class C { void M(bool a, bool b, bool c, int value, System.Collections.Generic.IEnumerable<int> values) { " + statements + " } }").GetRoot();
        return root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;
    }

    private static ExpressionSyntax GetMethodExpression(string expression)
    {
        var root = CSharpSyntaxTree.ParseText("class C { int M(bool a, bool b, bool c) => " + expression + "; }").GetRoot();
        return root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().ExpressionBody!.Expression;
    }
}
