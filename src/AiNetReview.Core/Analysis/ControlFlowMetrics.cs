namespace AiNetReview.Core.Analysis;

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>Measures the existing decision-counting rules for one executable body.</summary>
public static class ControlFlowMetrics
{
    /// <summary>Measures decisions in a block body or expression body.</summary>
    /// <param name="executableBody">A block body or the expression from an expression body.</param>
    /// <returns>The decision counts, maximum nesting, and first deepest decision.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="executableBody"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="executableBody"/> is not a block or expression.</exception>
    public static ControlFlowMeasurement Measure(SyntaxNode executableBody)
    {
        ArgumentNullException.ThrowIfNull(executableBody);
        if (executableBody is not (BlockSyntax or ExpressionSyntax))
        {
            throw new ArgumentException("The executable body must be a block or expression.", nameof(executableBody));
        }

        var visitor = new DecisionVisitor();
        visitor.Visit(executableBody);
        return new ControlFlowMeasurement(
            visitor.DecisionCount,
            visitor.DecisionConstructCount,
            visitor.MaxDecisionNesting,
            visitor.DeepestDecision);
    }

    private sealed class DecisionVisitor : CSharpSyntaxWalker
    {
        private int decisionDepth;

        public int DecisionCount { get; private set; }

        public int DecisionConstructCount { get; private set; }

        public int MaxDecisionNesting { get; private set; }

        public SyntaxNode? DeepestDecision { get; private set; }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            var baseDepth = decisionDepth;
            CountDecisionConstruct(baseDepth + 1, node);
            VisitAtDepth(node.Condition, baseDepth + 1);
            VisitAtDepth(node.Statement, baseDepth + 1);
            if (node.Else is { } elseClause)
            {
                if (elseClause.Statement is IfStatementSyntax elseIf)
                {
                    VisitIfAtDepth(elseIf, baseDepth);
                }
                else
                {
                    VisitAtDepth(elseClause.Statement, baseDepth + 1);
                }
            }
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            var baseDepth = decisionDepth;
            DecisionConstructCount++;
            VisitAtDepth(node.Expression, baseDepth);
            foreach (var section in node.Sections)
            {
                CountDecision(baseDepth + 1, section);
                foreach (var label in section.Labels)
                {
                    VisitAtDepth(label, baseDepth + 1);
                }

                foreach (var statement in section.Statements)
                {
                    VisitAtDepth(statement, baseDepth + 1);
                }
            }
        }

        public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            var baseDepth = decisionDepth;
            DecisionConstructCount++;
            VisitAtDepth(node.GoverningExpression, baseDepth);
            foreach (var arm in node.Arms)
            {
                CountDecision(baseDepth + 1, arm);
                VisitAtDepth(arm, baseDepth + 1);
            }
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node) => VisitDecisionNode(node);

        public override void VisitForStatement(ForStatementSyntax node) => VisitDecisionNode(node);

        public override void VisitForEachStatement(ForEachStatementSyntax node) => VisitDecisionNode(node);

        public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node) => VisitDecisionNode(node);

        public override void VisitWhileStatement(WhileStatementSyntax node) => VisitDecisionNode(node);

        public override void VisitDoStatement(DoStatementSyntax node) => VisitDecisionNode(node);

        public override void VisitCatchClause(CatchClauseSyntax node) => VisitDecisionNode(node);

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
        }

        public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
        }

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
        }

        private void VisitIfAtDepth(IfStatementSyntax node, int baseDepth)
        {
            var savedDepth = decisionDepth;
            decisionDepth = baseDepth;
            VisitIfStatement(node);
            decisionDepth = savedDepth;
        }

        private void VisitDecisionNode(SyntaxNode node)
        {
            var baseDepth = decisionDepth;
            CountDecisionConstruct(baseDepth + 1, node);
            VisitChildrenAtDepth(node, baseDepth + 1);
        }

        private void VisitAtDepth(SyntaxNode node, int depth)
        {
            var savedDepth = decisionDepth;
            decisionDepth = depth;
            Visit(node);
            decisionDepth = savedDepth;
        }

        private void VisitChildrenAtDepth(SyntaxNode node, int depth)
        {
            var savedDepth = decisionDepth;
            decisionDepth = depth;
            foreach (var child in node.ChildNodes())
            {
                Visit(child);
            }

            decisionDepth = savedDepth;
        }

        private void CountDecisionConstruct(int depth, SyntaxNode node)
        {
            DecisionConstructCount++;
            CountDecision(depth, node);
        }

        private void CountDecision(int depth, SyntaxNode node)
        {
            DecisionCount++;
            if (depth > MaxDecisionNesting)
            {
                MaxDecisionNesting = depth;
                DeepestDecision = node;
            }
        }
    }
}

/// <summary>Immutable measurements returned by <see cref="ControlFlowMetrics.Measure"/>.</summary>
public readonly record struct ControlFlowMeasurement(
    int DecisionCount,
    int DecisionConstructCount,
    int MaxDecisionNesting,
    SyntaxNode? DeepestDecision);
