namespace Lua.CodeAnalysis.Syntax.Nodes;

public record NumericForStatementNode(ReadOnlyMemory<char> VariableName, ExpressionNode InitNode, ExpressionNode LimitNode, ExpressionNode? StepNode, StatementNode[] StatementNodes, SourcePosition Position,SourcePosition DoPosition) : StatementNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitNumericForStatementNode(this, context);
    }
}