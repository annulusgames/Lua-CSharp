namespace Lua.CodeAnalysis.Syntax.Nodes;

public record FunctionDeclarationExpressionNode(IdentifierNode[] ParameterNodes, SyntaxNode[] Nodes, bool HasVariableArguments, SourcePosition Position, int LineDefined,SourcePosition EndPosition) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitFunctionDeclarationExpressionNode(this, context);
    }
}