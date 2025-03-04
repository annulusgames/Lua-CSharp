using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Lua.Internal;
using Lua.CodeAnalysis.Syntax.Nodes;
using System.Globalization;

namespace Lua.CodeAnalysis.Syntax;

public ref struct Parser
{
    public string? ChunkName { get; init; }

    PooledList<SyntaxToken> tokens;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(SyntaxToken token) => tokens.Add(token);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        tokens.Dispose();
    }

    public LuaSyntaxTree Parse()
    {
        using var root = new PooledList<SyntaxNode>(64);

        var enumerator = new SyntaxTokenEnumerator(tokens.AsSpan());
        while (enumerator.MoveNext())
        {
            if (enumerator.Current.Type is SyntaxTokenType.EndOfLine or SyntaxTokenType.SemiColon) continue;

            var node = ParseStatement(ref enumerator);
            root.Add(node);
        }
        var tokensSpan = tokens.AsSpan();
        var lastToken = tokensSpan[0];
        for (int i = tokensSpan.Length-1; 0<i;i--)
        {
            var t = tokensSpan[i];
            if (t.Type is not SyntaxTokenType.EndOfLine)
            {
                lastToken = t;
                break;
            }
        }

        var tree = new LuaSyntaxTree(root.AsSpan().ToArray(),lastToken.Position);
        Dispose();

        return tree;
    }

    StatementNode ParseStatement(ref SyntaxTokenEnumerator enumerator)
    {
        switch (enumerator.Current.Type)
        {
            case SyntaxTokenType.LParen:
            case SyntaxTokenType.Identifier:
                {
                    var firstExpression = ParseExpression(ref enumerator, OperatorPrecedence.NonOperator);

                    switch (firstExpression)
                    {
                        case CallFunctionExpressionNode callFunctionExpression:
                            return new CallFunctionStatementNode(callFunctionExpression);
                        case CallTableMethodExpressionNode callTableMethodExpression:
                            return new CallTableMethodStatementNode(callTableMethodExpression);
                        default:
                            if (enumerator.GetNext(true).Type is SyntaxTokenType.Comma or SyntaxTokenType.Assignment)
                            {
                                // skip ','
                                MoveNextWithValidation(ref enumerator);
                                enumerator.SkipEoL();

                                return ParseAssignmentStatement(firstExpression, ref enumerator);
                            }

                            break;
                    }
                }
                break;
            case SyntaxTokenType.Return:
                return ParseReturnStatement(ref enumerator);
            case SyntaxTokenType.Do:
                return ParseDoStatement(ref enumerator);
            case SyntaxTokenType.Goto:
                return ParseGotoStatement(ref enumerator);
            case SyntaxTokenType.Label:
                return new LabelStatementNode(enumerator.Current.Text, enumerator.Current.Position);
            case SyntaxTokenType.If:
                return ParseIfStatement(ref enumerator);
            case SyntaxTokenType.While:
                return ParseWhileStatement(ref enumerator);
            case SyntaxTokenType.Repeat:
                return ParseRepeatStatement(ref enumerator);
            case SyntaxTokenType.For:
                {
                    // skip 'for' keyword
                    var forToken = enumerator.Current;
                    MoveNextWithValidation(ref enumerator);
                    enumerator.SkipEoL();

                    if (enumerator.GetNext(true).Type is SyntaxTokenType.Assignment)
                    {
                        return ParseNumericForStatement(ref enumerator, forToken);
                    }
                    else
                    {
                        return ParseGenericForStatement(ref enumerator, forToken);
                    }
                }
            case SyntaxTokenType.Break:
                return new BreakStatementNode(enumerator.Current.Position);
            case SyntaxTokenType.Local:
                {
                    // skip 'local' keyword
                    CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.Local, out var localToken);

                    // local function
                    if (enumerator.Current.Type is SyntaxTokenType.Function)
                    {
                        // skip 'function' keyword
                        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.Function, out var functionToken);
                        enumerator.SkipEoL();

                        return ParseLocalFunctionDeclarationStatement(ref enumerator, functionToken);
                    }

                    CheckCurrent(ref enumerator, SyntaxTokenType.Identifier);

                    var nextType = enumerator.GetNext().Type;

                    if (nextType is SyntaxTokenType.Comma or SyntaxTokenType.Assignment)
                    {
                        return ParseLocalAssignmentStatement(ref enumerator, localToken);
                    }
                    else if (nextType is SyntaxTokenType.EndOfLine or SyntaxTokenType.SemiColon)
                    {
                        return new LocalAssignmentStatementNode([new IdentifierNode(enumerator.Current.Text, enumerator.Current.Position)], [], localToken.Position);
                    }
                }
                break;
            case SyntaxTokenType.Function:
                {
                    // skip 'function' keyword
                    CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.Function, out var functionToken);
                    enumerator.SkipEoL();

                    if (enumerator.GetNext(true).Type is SyntaxTokenType.Dot or SyntaxTokenType.Colon)
                    {
                        return ParseTableMethodDeclarationStatement(ref enumerator, functionToken);
                    }
                    else
                    {
                        return ParseFunctionDeclarationStatement(ref enumerator, functionToken);
                    }
                }
        }

        LuaParseException.UnexpectedToken(ChunkName, enumerator.Current.Position, enumerator.Current);
        return default!;
    }

    ReturnStatementNode ParseReturnStatement(ref SyntaxTokenEnumerator enumerator)
    {
        // skip 'return' keyword
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.Return, out var returnToken);

        // parse parameters
        var expressions = ParseExpressionList(ref enumerator);

        return new ReturnStatementNode(expressions, returnToken.Position);
    }

    DoStatementNode ParseDoStatement(ref SyntaxTokenEnumerator enumerator)
    {
        // check 'do' keyword
        CheckCurrent(ref enumerator, SyntaxTokenType.Do);
        var doToken = enumerator.Current;

        // parse statements
        var statements = ParseStatementList(ref enumerator, SyntaxTokenType.End, out _);

        return new DoStatementNode(statements, doToken.Position);
    }

    GotoStatementNode ParseGotoStatement(ref SyntaxTokenEnumerator enumerator)
    {
        // skip 'goto' keyword
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.Goto, out var gotoToken);

        CheckCurrent(ref enumerator, SyntaxTokenType.Identifier);
        return new GotoStatementNode(enumerator.Current.Text, gotoToken.Position);
    }

    AssignmentStatementNode ParseAssignmentStatement(ExpressionNode firstExpression, ref SyntaxTokenEnumerator enumerator)
    {
        // parse leftNodes
        using var leftNodes = new PooledList<SyntaxNode>(8);
        leftNodes.Add(firstExpression);

        while (enumerator.Current.Type == SyntaxTokenType.Comma)
        {
            // skip ','
            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();

            // parse identifier
            CheckCurrent(ref enumerator, SyntaxTokenType.Identifier);
            leftNodes.Add(ParseExpression(ref enumerator, OperatorPrecedence.NonOperator));

            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();
        }

        // skip '='
        if (enumerator.Current.Type is not SyntaxTokenType.Assignment)
        {
            enumerator.MovePrevious();
            return new AssignmentStatementNode(leftNodes.AsSpan().ToArray(), [], firstExpression.Position);
        }

        MoveNextWithValidation(ref enumerator);

        // parse expressions
        var expressions = ParseExpressionList(ref enumerator);

        return new AssignmentStatementNode(leftNodes.AsSpan().ToArray(), expressions, firstExpression.Position);
    }

    LocalAssignmentStatementNode ParseLocalAssignmentStatement(ref SyntaxTokenEnumerator enumerator, SyntaxToken localToken)
    {
        // parse identifiers
        var identifiers = ParseIdentifierList(ref enumerator);

        // skip '='
        if (enumerator.Current.Type is not SyntaxTokenType.Assignment)
        {
            enumerator.MovePrevious();
            return new LocalAssignmentStatementNode(identifiers, [], localToken.Position);
        }

        MoveNextWithValidation(ref enumerator);

        // parse expressions
        var expressions = ParseExpressionList(ref enumerator);

        return new LocalAssignmentStatementNode(identifiers, expressions, localToken.Position);
    }

    IfStatementNode ParseIfStatement(ref SyntaxTokenEnumerator enumerator)
    {
        // skip 'if' keyword
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.If, out var ifToken);
        enumerator.SkipEoL();

        // parse condition
        var condition = ParseExpression(ref enumerator, GetPrecedence(enumerator.Current.Type));
        MoveNextWithValidation(ref enumerator);
        enumerator.SkipEoL();

        // skip 'then' keyword
        CheckCurrent(ref enumerator, SyntaxTokenType.Then);
        var thenToken = enumerator.Current;

        using var builder = new PooledList<StatementNode>(64);
        using var elseIfBuilder = new PooledList<IfStatementNode.ConditionAndThenNodes>(64);

        IfStatementNode.ConditionAndThenNodes ifNodes = default!;
        StatementNode[] elseNodes = [];

        // if = 0, elseif = 1, else = 2
        var state = 0;

        // parse statements
        while (true)
        {
            if (!enumerator.MoveNext())
            {
                LuaParseException.ExpectedToken(ChunkName, enumerator.Current.Position, SyntaxTokenType.End);
            }

            var tokenType = enumerator.Current.Type;

            if (tokenType is SyntaxTokenType.EndOfLine or SyntaxTokenType.SemiColon)
            {
                continue;
            }

            if (tokenType is SyntaxTokenType.ElseIf or SyntaxTokenType.Else or SyntaxTokenType.End)
            {
                switch (state)
                {
                    case 0:
                        ifNodes = new()
                        {
                            Position = thenToken.Position,
                            ConditionNode = condition,
                            ThenNodes = builder.AsSpan().ToArray(),
                        };
                        builder.Clear();
                        break;
                    case 1:
                        elseIfBuilder.Add(new()
                        {
                            Position = thenToken.Position,
                            ConditionNode = condition,
                            ThenNodes = builder.AsSpan().ToArray(),
                        });
                        builder.Clear();
                        break;
                    case 2:
                        elseNodes = builder.AsSpan().ToArray();
                        break;
                }

                if (tokenType is SyntaxTokenType.ElseIf)
                {
                    // skip 'elseif' keywords
                    MoveNextWithValidation(ref enumerator);
                    enumerator.SkipEoL();

                    // parse condition
                    condition = ParseExpression(ref enumerator, GetPrecedence(enumerator.Current.Type));
                    MoveNextWithValidation(ref enumerator);
                    enumerator.SkipEoL();

                    // check 'then' keyword
                    CheckCurrent(ref enumerator, SyntaxTokenType.Then);
                    thenToken = enumerator.Current;
                    // set elseif state
                    state = 1;

                    continue;
                }
                else if (tokenType is SyntaxTokenType.Else)
                {
                    // set else state
                    state = 2;

                    continue;
                }
                else if (tokenType is SyntaxTokenType.End)
                {
                    goto RETURN;
                }
            }

            var node = ParseStatement(ref enumerator);
            builder.Add(node);
        }

    RETURN:
        return new IfStatementNode(ifNodes, elseIfBuilder.AsSpan().ToArray(), elseNodes, ifToken.Position);
    }

    WhileStatementNode ParseWhileStatement(ref SyntaxTokenEnumerator enumerator)
    {
        // skip 'while' keyword
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.While, out var whileToken);
        enumerator.SkipEoL();

        // parse condition
        var condition = ParseExpression(ref enumerator, GetPrecedence(enumerator.Current.Type));
        MoveNextWithValidation(ref enumerator);
        enumerator.SkipEoL();

        // skip 'do' keyword
        CheckCurrent(ref enumerator, SyntaxTokenType.Do);

        // parse statements
        var statements = ParseStatementList(ref enumerator, SyntaxTokenType.End, out _);

        return new WhileStatementNode(condition, statements, whileToken.Position);
    }

    RepeatStatementNode ParseRepeatStatement(ref SyntaxTokenEnumerator enumerator)
    {
        // skip 'repeat' keyword
        CheckCurrent(ref enumerator, SyntaxTokenType.Repeat);
        var repeatToken = enumerator.Current;

        // parse statements
        var statements = ParseStatementList(ref enumerator, SyntaxTokenType.Until, out _);

        // skip 'until keyword'
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.Until, out _);
        enumerator.SkipEoL();

        // parse condition
        var condition = ParseExpression(ref enumerator, GetPrecedence(enumerator.Current.Type));

        return new RepeatStatementNode(condition, statements, repeatToken.Position);
    }

    NumericForStatementNode ParseNumericForStatement(ref SyntaxTokenEnumerator enumerator, SyntaxToken forToken)
    {
        // parse variable name
        CheckCurrent(ref enumerator, SyntaxTokenType.Identifier);
        var varName = enumerator.Current.Text;
        MoveNextWithValidation(ref enumerator);
        enumerator.SkipEoL();

        // skip '='
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.Assignment, out _);
        enumerator.SkipEoL();

        // parse initial value
        var initialValueNode = ParseExpression(ref enumerator, OperatorPrecedence.NonOperator);
        MoveNextWithValidation(ref enumerator);
        enumerator.SkipEoL();

        // skip ','
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.Comma, out _);
        enumerator.SkipEoL();

        // parse limit
        var limitNode = ParseExpression(ref enumerator, OperatorPrecedence.NonOperator);
        MoveNextWithValidation(ref enumerator);
        enumerator.SkipEoL();

        // parse stepNode
        ExpressionNode? stepNode = null;
        if (enumerator.Current.Type is SyntaxTokenType.Comma)
        {
            // skip ','
            enumerator.MoveNext();

            // parse step
            stepNode = ParseExpression(ref enumerator, OperatorPrecedence.NonOperator);
            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();
        }

        // skip 'do' keyword
        CheckCurrent(ref enumerator, SyntaxTokenType.Do);
        var doToken = enumerator.Current;

        // parse statements
        var statements = ParseStatementList(ref enumerator, SyntaxTokenType.End, out _);

        return new NumericForStatementNode(varName, initialValueNode, limitNode, stepNode, statements, forToken.Position, doToken.Position);
    }

    GenericForStatementNode ParseGenericForStatement(ref SyntaxTokenEnumerator enumerator, SyntaxToken forToken)
    {
        var identifiers = ParseIdentifierList(ref enumerator);
        enumerator.SkipEoL();

        // skip 'in' keyword
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.In, out _);
        enumerator.SkipEoL();
        var iteratorToken = enumerator.Current;
        var expressions = ParseExpressionList(ref enumerator);
        MoveNextWithValidation(ref enumerator);
        enumerator.SkipEoL();

        // skip 'do' keyword
        CheckCurrent(ref enumerator, SyntaxTokenType.Do);
        var doToken = enumerator.Current;
        // parse statements
        var statements = ParseStatementList(ref enumerator, SyntaxTokenType.End, out var endToken);

        return new GenericForStatementNode(identifiers, expressions, statements, iteratorToken.Position, doToken.Position, endToken.Position);
    }

    FunctionDeclarationStatementNode ParseFunctionDeclarationStatement(ref SyntaxTokenEnumerator enumerator, SyntaxToken functionToken)
    {
        var (Name, Identifiers, Statements, HasVariableArgments, LineDefined, EndPosition) = ParseFunctionDeclarationCore(ref enumerator, false);
        return new FunctionDeclarationStatementNode(Name, Identifiers, Statements, HasVariableArgments, functionToken.Position, LineDefined, EndPosition);
    }

    LocalFunctionDeclarationStatementNode ParseLocalFunctionDeclarationStatement(ref SyntaxTokenEnumerator enumerator, SyntaxToken functionToken)
    {
        var (Name, Identifiers, Statements, HasVariableArgments, LineDefined, EndPosition) = ParseFunctionDeclarationCore(ref enumerator, false);
        return new LocalFunctionDeclarationStatementNode(Name, Identifiers, Statements, HasVariableArgments, functionToken.Position, LineDefined, EndPosition);
    }

    (ReadOnlyMemory<char> Name, IdentifierNode[] Identifiers, StatementNode[] Statements, bool HasVariableArgments, int LineDefined, SourcePosition EndPosition) ParseFunctionDeclarationCore(ref SyntaxTokenEnumerator enumerator, bool isAnonymous)
    {
        ReadOnlyMemory<char> name;

        if (isAnonymous)
        {
            name = ReadOnlyMemory<char>.Empty;
        }
        else
        {
            // parse function name
            CheckCurrent(ref enumerator, SyntaxTokenType.Identifier);
            name = enumerator.Current.Text;

            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();
        }

        // skip '('
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.LParen, out var leftParenToken);
        enumerator.SkipEoL();

        // parse parameters
        var identifiers = enumerator.Current.Type is SyntaxTokenType.Identifier
            ? ParseIdentifierList(ref enumerator)
            : [];

        // check variable arguments
        var hasVarArg = enumerator.Current.Type is SyntaxTokenType.VarArg;
        if (hasVarArg) enumerator.MoveNext();

        // skip ')'
        CheckCurrent(ref enumerator, SyntaxTokenType.RParen);

        // parse statements
        var statements = ParseStatementList(ref enumerator, SyntaxTokenType.End, out var endToken);

        return (name, identifiers, statements, hasVarArg, leftParenToken.Position.Line, endToken.Position);
    }

    TableMethodDeclarationStatementNode ParseTableMethodDeclarationStatement(ref SyntaxTokenEnumerator enumerator, SyntaxToken functionToken)
    {
        using var names = new PooledList<IdentifierNode>(32);
        var hasSelfParameter = false;
        SyntaxToken leftParenToken;
        while (true)
        {
            CheckCurrent(ref enumerator, SyntaxTokenType.Identifier);
            names.Add(new IdentifierNode(enumerator.Current.Text, enumerator.Current.Position));
            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();

            if (enumerator.Current.Type is SyntaxTokenType.Dot or SyntaxTokenType.Colon)
            {
                if (hasSelfParameter)
                {
                    LuaParseException.UnexpectedToken(ChunkName, enumerator.Current.Position, enumerator.Current);
                }

                hasSelfParameter = enumerator.Current.Type is SyntaxTokenType.Colon;

                MoveNextWithValidation(ref enumerator);
                enumerator.SkipEoL();
            }
            else if (enumerator.Current.Type is SyntaxTokenType.LParen)
            {
                leftParenToken = enumerator.Current;
                // skip '('
                MoveNextWithValidation(ref enumerator);
                enumerator.SkipEoL();
                break;
            }
        }

        // parse parameters
        var identifiers = enumerator.Current.Type is SyntaxTokenType.Identifier
            ? ParseIdentifierList(ref enumerator)
            : [];

        // check variable arguments
        var hasVarArg = enumerator.Current.Type is SyntaxTokenType.VarArg;
        if (hasVarArg) enumerator.MoveNext();

        // skip ')'
        CheckCurrent(ref enumerator, SyntaxTokenType.RParen);

        // parse statements
        var statements = ParseStatementList(ref enumerator, SyntaxTokenType.End, out var endToken);

        return new TableMethodDeclarationStatementNode(names.AsSpan().ToArray(), identifiers, statements, hasVarArg, hasSelfParameter, functionToken.Position, leftParenToken.Position.Line, endToken.Position);
    }

    bool TryParseExpression(ref SyntaxTokenEnumerator enumerator, OperatorPrecedence precedence, [NotNullWhen(true)] out ExpressionNode? result)
    {
        result = enumerator.Current.Type switch
        {
            SyntaxTokenType.Identifier => enumerator.GetNext(true).Type switch
            {
                SyntaxTokenType.LParen or SyntaxTokenType.String or SyntaxTokenType.RawString => ParseCallFunctionExpression(ref enumerator, null),
                SyntaxTokenType.LSquare or SyntaxTokenType.Dot or SyntaxTokenType.Colon => ParseTableAccessExpression(ref enumerator, null),
                _ => new IdentifierNode(enumerator.Current.Text, enumerator.Current.Position),
            },
            SyntaxTokenType.Number => new NumericLiteralNode(ConvertTextToNumber(enumerator.Current.Text.Span), enumerator.Current.Position),
            SyntaxTokenType.String => new StringLiteralNode(enumerator.Current.Text, true, enumerator.Current.Position),
            SyntaxTokenType.RawString => new StringLiteralNode(enumerator.Current.Text, false, enumerator.Current.Position),
            SyntaxTokenType.True => new BooleanLiteralNode(true, enumerator.Current.Position),
            SyntaxTokenType.False => new BooleanLiteralNode(false, enumerator.Current.Position),
            SyntaxTokenType.Nil => new NilLiteralNode(enumerator.Current.Position),
            SyntaxTokenType.VarArg => new VariableArgumentsExpressionNode(enumerator.Current.Position),
            SyntaxTokenType.Subtraction => ParseMinusNumber(ref enumerator),
            SyntaxTokenType.Not or SyntaxTokenType.Length => ParseUnaryExpression(ref enumerator, enumerator.Current),
            SyntaxTokenType.LParen => ParseGroupedExpression(ref enumerator),
            SyntaxTokenType.LCurly => ParseTableConstructorExpression(ref enumerator),
            SyntaxTokenType.Function => ParseFunctionDeclarationExpression(ref enumerator),
            _ => null,
        };

        if (result == null) return false;

        // nested table access & function call
    RECURSIVE:
        enumerator.SkipEoL();

        var nextType = enumerator.GetNext().Type;
        if (nextType is SyntaxTokenType.LSquare or SyntaxTokenType.Dot or SyntaxTokenType.Colon)
        {
            MoveNextWithValidation(ref enumerator);
            result = ParseTableAccessExpression(ref enumerator, result);
            goto RECURSIVE;
        }
        else if (nextType is SyntaxTokenType.LParen or SyntaxTokenType.String or SyntaxTokenType.RawString or SyntaxTokenType.LCurly)
        {
            MoveNextWithValidation(ref enumerator);
            result = ParseCallFunctionExpression(ref enumerator, result);
            goto RECURSIVE;
        }

        // binary expression
        while (true)
        {
            var opPrecedence = GetPrecedence(enumerator.GetNext(true).Type);
            if (precedence >= opPrecedence) break;

            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();
            result = ParseBinaryExpression(ref enumerator, opPrecedence, result);

            enumerator.SkipEoL();
        }

        return true;
    }

    ExpressionNode ParseExpression(ref SyntaxTokenEnumerator enumerator, OperatorPrecedence precedence)
    {
        if (!TryParseExpression(ref enumerator, precedence, out var result))
        {
            throw new LuaParseException(ChunkName, enumerator.Current.Position, $"Unexpected token <{enumerator.Current.Type}>");
        }

        return result;
    }

    ExpressionNode ParseMinusNumber(ref SyntaxTokenEnumerator enumerator)
    {
        var token = enumerator.Current;
        if (enumerator.GetNext(true).Type is SyntaxTokenType.Number)
        {
            enumerator.MoveNext();
            enumerator.SkipEoL();

            return new NumericLiteralNode(-ConvertTextToNumber(enumerator.Current.Text.Span), token.Position);
        }
        else
        {
            return ParseUnaryExpression(ref enumerator, token);
        }
    }

    UnaryExpressionNode ParseUnaryExpression(ref SyntaxTokenEnumerator enumerator, SyntaxToken operatorToken)
    {
        var operatorType = enumerator.Current.Type switch
        {
            SyntaxTokenType.Subtraction => UnaryOperator.Negate,
            SyntaxTokenType.Not => UnaryOperator.Not,
            SyntaxTokenType.Length => UnaryOperator.Length,
            _ => throw new LuaParseException(ChunkName, operatorToken.Position, $"unexpected symbol near '{enumerator.Current.Text}'"),
        };

        MoveNextWithValidation(ref enumerator);
        var right = ParseExpression(ref enumerator, OperatorPrecedence.Unary);

        return new UnaryExpressionNode(operatorType, right, operatorToken.Position);
    }

    BinaryExpressionNode ParseBinaryExpression(ref SyntaxTokenEnumerator enumerator, OperatorPrecedence precedence, ExpressionNode left)
    {
        var operatorToken = enumerator.Current;
        var operatorType = operatorToken.Type switch
        {
            SyntaxTokenType.Addition => BinaryOperator.Addition,
            SyntaxTokenType.Subtraction => BinaryOperator.Subtraction,
            SyntaxTokenType.Multiplication => BinaryOperator.Multiplication,
            SyntaxTokenType.Division => BinaryOperator.Division,
            SyntaxTokenType.Modulo => BinaryOperator.Modulo,
            SyntaxTokenType.Exponentiation => BinaryOperator.Exponentiation,
            SyntaxTokenType.Equality => BinaryOperator.Equality,
            SyntaxTokenType.Inequality => BinaryOperator.Inequality,
            SyntaxTokenType.LessThan => BinaryOperator.LessThan,
            SyntaxTokenType.LessThanOrEqual => BinaryOperator.LessThanOrEqual,
            SyntaxTokenType.GreaterThan => BinaryOperator.GreaterThan,
            SyntaxTokenType.GreaterThanOrEqual => BinaryOperator.GreaterThanOrEqual,
            SyntaxTokenType.And => BinaryOperator.And,
            SyntaxTokenType.Or => BinaryOperator.Or,
            SyntaxTokenType.Concat => BinaryOperator.Concat,
            _ => throw new LuaParseException(ChunkName, enumerator.Current.Position, $"unexpected symbol near '{enumerator.Current.Text}'"),
        };

        enumerator.SkipEoL();
        MoveNextWithValidation(ref enumerator);
        enumerator.SkipEoL();

        var right = ParseExpression(ref enumerator, precedence);

        return new BinaryExpressionNode(operatorType, left, right, operatorToken.Position);
    }

    TableConstructorExpressionNode ParseTableConstructorExpression(ref SyntaxTokenEnumerator enumerator)
    {
        CheckCurrent(ref enumerator, SyntaxTokenType.LCurly);
        var startToken = enumerator.Current;

        using var items = new PooledList<TableConstructorField>(16);

        while (enumerator.MoveNext())
        {
            var currentToken = enumerator.Current;
            switch (currentToken.Type)
            {
                case SyntaxTokenType.RCurly:
                    goto RETURN;
                case SyntaxTokenType.EndOfLine:
                case SyntaxTokenType.SemiColon:
                case SyntaxTokenType.Comma:
                    continue;
                case SyntaxTokenType.LSquare:
                    // general style ([key] = value)
                    enumerator.MoveNext();

                    var keyExpression = ParseExpression(ref enumerator, OperatorPrecedence.NonOperator);
                    enumerator.MoveNext();

                    // skip '] ='
                    CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.RSquare, out _);
                    CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.Assignment, out _);

                    var valueExpression = ParseExpression(ref enumerator, OperatorPrecedence.NonOperator);

                    items.Add(new GeneralTableConstructorField(keyExpression, valueExpression, currentToken.Position));

                    break;
                case SyntaxTokenType.Identifier when enumerator.GetNext(true).Type is SyntaxTokenType.Assignment:
                    // record style (key = value)
                    var name = enumerator.Current.Text;

                    // skip key and '='
                    enumerator.MoveNext();
                    enumerator.MoveNext();

                    var expression = ParseExpression(ref enumerator, OperatorPrecedence.NonOperator);

                    items.Add(new RecordTableConstructorField(name.ToString(), expression, currentToken.Position));
                    break;
                default:
                    // list style
                    items.Add(new ListTableConstructorField(ParseExpression(ref enumerator, OperatorPrecedence.NonOperator), currentToken.Position));
                    break;
            }
        }

    RETURN:
        return new TableConstructorExpressionNode(items.AsSpan().ToArray(), startToken.Position);
    }

    ExpressionNode ParseTableAccessExpression(ref SyntaxTokenEnumerator enumerator, ExpressionNode? parentTable)
    {
        IdentifierNode? identifier = null;
        if (parentTable == null)
        {
            // parse identifier
            CheckCurrent(ref enumerator, SyntaxTokenType.Identifier);
            identifier = new IdentifierNode(enumerator.Current.Text, enumerator.Current.Position);
            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();
        }

        ExpressionNode result;
        var current = enumerator.Current;
        if (current.Type is SyntaxTokenType.LSquare)
        {
            // indexer access -- table[key]

            // skip '['
            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();

            // parse key expression
            var keyExpression = ParseExpression(ref enumerator, OperatorPrecedence.NonOperator);
            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();

            // check ']'
            CheckCurrent(ref enumerator, SyntaxTokenType.RSquare);

            result = new TableIndexerAccessExpressionNode(identifier ?? parentTable!, keyExpression, current.Position);
        }
        else if (current.Type is SyntaxTokenType.Dot)
        {
            // member access -- table.key

            // skip '.'
            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();

            // parse identifier
            CheckCurrent(ref enumerator, SyntaxTokenType.Identifier);
            var key = enumerator.Current.Text.ToString();

            result = new TableMemberAccessExpressionNode(identifier ?? parentTable!, key, current.Position);
        }
        else if (current.Type is SyntaxTokenType.Colon)
        {
            // self method call -- table:method(arg0, arg1, ...)

            // skip ':'
            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();

            // parse identifier
            CheckCurrent(ref enumerator, SyntaxTokenType.Identifier);
            var methodName = enumerator.Current.Text;
            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();

            // parse arguments
            var arguments = ParseCallFunctionArguments(ref enumerator);
            result = new CallTableMethodExpressionNode(identifier ?? parentTable!, methodName.ToString(), arguments, current.Position);
        }
        else
        {
            LuaParseException.SyntaxError(ChunkName, current.Position, current);
            return null!; // dummy
        }

        return result;
    }

    GroupedExpressionNode ParseGroupedExpression(ref SyntaxTokenEnumerator enumerator)
    {
        // skip '('
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.LParen, out var lParen);
        enumerator.SkipEoL();

        var expression = ParseExpression(ref enumerator, GetPrecedence(enumerator.Current.Type));
        MoveNextWithValidation(ref enumerator);

        // check ')'
        CheckCurrent(ref enumerator, SyntaxTokenType.RParen);

        return new GroupedExpressionNode(expression, lParen.Position);
    }

    ExpressionNode ParseCallFunctionExpression(ref SyntaxTokenEnumerator enumerator, ExpressionNode? function)
    {
        // parse name
        if (function == null)
        {
            CheckCurrent(ref enumerator, SyntaxTokenType.Identifier);
            function = new IdentifierNode(enumerator.Current.Text, enumerator.Current.Position);
            enumerator.MoveNext();
            enumerator.SkipEoL();
        }

        // parse parameters
        var parameters = ParseCallFunctionArguments(ref enumerator);

        return new CallFunctionExpressionNode(function, parameters);
    }

    FunctionDeclarationExpressionNode ParseFunctionDeclarationExpression(ref SyntaxTokenEnumerator enumerator)
    {
        // skip 'function' keyword
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.Function, out var functionToken);
        enumerator.SkipEoL();

        var (_, Identifiers, Statements, HasVariableArgments, LineDefined, LastLineDefined) = ParseFunctionDeclarationCore(ref enumerator, true);
        return new FunctionDeclarationExpressionNode(Identifiers, Statements, HasVariableArgments, functionToken.Position, LineDefined, LastLineDefined);
    }

    ExpressionNode[] ParseCallFunctionArguments(ref SyntaxTokenEnumerator enumerator)
    {
        if (enumerator.Current.Type is SyntaxTokenType.String)
        {
            return [new StringLiteralNode(enumerator.Current.Text, true, enumerator.Current.Position)];
        }
        else if (enumerator.Current.Type is SyntaxTokenType.RawString)
        {
            return [new StringLiteralNode(enumerator.Current.Text, false, enumerator.Current.Position)];
        }
        else if (enumerator.Current.Type is SyntaxTokenType.LCurly)
        {
            return [ParseTableConstructorExpression(ref enumerator)];
        }

        // check and skip '('
        CheckCurrentAndSkip(ref enumerator, SyntaxTokenType.LParen, out _);

        ExpressionNode[] arguments;
        if (enumerator.Current.Type is SyntaxTokenType.RParen)
        {
            // parameterless
            arguments = [];
        }
        else
        {
            // parse arguments
            arguments = ParseExpressionList(ref enumerator);
            enumerator.SkipEoL();

            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();

            // check ')'
            CheckCurrent(ref enumerator, SyntaxTokenType.RParen);
        }

        return arguments;
    }

    ExpressionNode[] ParseExpressionList(ref SyntaxTokenEnumerator enumerator)
    {
        using var builder = new PooledList<ExpressionNode>(8);

        while (true)
        {
            enumerator.SkipEoL();

            if (!TryParseExpression(ref enumerator, OperatorPrecedence.NonOperator, out var expression))
            {
                enumerator.MovePrevious();
                break;
            }

            builder.Add(expression);

            enumerator.SkipEoL();
            if (enumerator.GetNext().Type != SyntaxTokenType.Comma) break;

            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();

            if (!enumerator.MoveNext()) break;
        }

        return builder.AsSpan().ToArray();
    }

    IdentifierNode[] ParseIdentifierList(ref SyntaxTokenEnumerator enumerator)
    {
        using var buffer = new PooledList<IdentifierNode>(8);

        while (true)
        {
            if (enumerator.Current.Type != SyntaxTokenType.Identifier) break;
            var identifier = new IdentifierNode(enumerator.Current.Text, enumerator.Current.Position);
            buffer.Add(identifier);

            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();

            if (enumerator.Current.Type != SyntaxTokenType.Comma) break;

            MoveNextWithValidation(ref enumerator);
            enumerator.SkipEoL();
        }

        return buffer.AsSpan().ToArray();
    }

    StatementNode[] ParseStatementList(ref SyntaxTokenEnumerator enumerator, SyntaxTokenType endTokenType, out SyntaxToken endToken)
    {
        using var statements = new PooledList<StatementNode>(64);

        // parse statements
        while (enumerator.MoveNext())
        {
            if (enumerator.Current.Type == endTokenType) break;
            if (enumerator.Current.Type is SyntaxTokenType.EndOfLine or SyntaxTokenType.SemiColon) continue;

            var node = ParseStatement(ref enumerator);
            statements.Add(node);
        }

        endToken = enumerator.Current;

        return statements.AsSpan().ToArray();
    }

    void CheckCurrentAndSkip(ref SyntaxTokenEnumerator enumerator, SyntaxTokenType expectedToken, out SyntaxToken token)
    {
        CheckCurrent(ref enumerator, expectedToken);
        token = enumerator.Current;
        MoveNextWithValidation(ref enumerator);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void CheckCurrent(ref SyntaxTokenEnumerator enumerator, SyntaxTokenType expectedToken)
    {
        if (enumerator.Current.Type != expectedToken)
        {
            LuaParseException.ExpectedToken(ChunkName, enumerator.Current.Position, expectedToken);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void MoveNextWithValidation(ref SyntaxTokenEnumerator enumerator)
    {
        if (!enumerator.MoveNext()) LuaParseException.SyntaxError(ChunkName, enumerator.Current.Position, enumerator.Current);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static OperatorPrecedence GetPrecedence(SyntaxTokenType type)
    {
        return type switch
        {
            SyntaxTokenType.Addition or SyntaxTokenType.Subtraction => OperatorPrecedence.Addition,
            SyntaxTokenType.Multiplication or SyntaxTokenType.Division or SyntaxTokenType.Modulo => OperatorPrecedence.Multiplication,
            SyntaxTokenType.Equality or SyntaxTokenType.Inequality or SyntaxTokenType.LessThan or SyntaxTokenType.LessThanOrEqual or SyntaxTokenType.GreaterThan or SyntaxTokenType.GreaterThanOrEqual => OperatorPrecedence.Relational,
            SyntaxTokenType.Concat => OperatorPrecedence.Concat,
            SyntaxTokenType.Exponentiation => OperatorPrecedence.Exponentiation,
            SyntaxTokenType.And => OperatorPrecedence.And,
            SyntaxTokenType.Or => OperatorPrecedence.Or,
            _ => OperatorPrecedence.NonOperator,
        };
    }

    static double ConvertTextToNumber(ReadOnlySpan<char> text)
    {
        if (text.Length > 2 && text[0] is '0' && text[1] is 'x' or 'X')
        {
            return HexConverter.ToDouble(text);
        }
        else
        {
            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}