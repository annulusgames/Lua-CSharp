namespace Lua.CodeAnalysis.Syntax.Nodes;

public record GenericForStatementNode(IdentifierNode[] Names, ExpressionNode[] ExpressionNodes, StatementNode[] StatementNodes, SourcePosition Position, SourcePosition DoPosition, SourcePosition EndPosition) : StatementNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitGenericForStatementNode(this, context);
    }
}