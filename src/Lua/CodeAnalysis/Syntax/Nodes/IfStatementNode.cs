namespace Lua.CodeAnalysis.Syntax.Nodes;

public record IfStatementNode(IfStatementNode.ConditionAndThenNodes IfNode, IfStatementNode.ConditionAndThenNodes[] ElseIfNodes, StatementNode[] ElseNodes, SourcePosition Position) : StatementNode(Position)
{
    public record ConditionAndThenNodes
    {
        public SourcePosition Position;
        public required ExpressionNode ConditionNode;
        public required StatementNode[] ThenNodes;
    }

    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitIfStatementNode(this, context);
    }
}