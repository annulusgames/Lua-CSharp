namespace Lua.CodeAnalysis.Syntax;

public record LuaSyntaxTree(SyntaxNode[] Nodes,SourcePosition Position) : SyntaxNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitSyntaxTree(this, context);
    }

    public static LuaSyntaxTree Parse(string source, string? chunkName = null)
    {
        var lexer = new Lexer
        {
            Source = source.AsMemory(),
            ChunkName = chunkName,
        };

        var parser = new Parser
        {
            ChunkName = chunkName
        };

        while (lexer.MoveNext())
        {
            parser.Add(lexer.Current);
        }

        return parser.Parse();
    }
}