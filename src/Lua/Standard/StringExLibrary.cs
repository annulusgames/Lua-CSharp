using System.Text;
using Lua.Internal;

namespace Lua.Standard;

public sealed class StringExLibrary
{
    public static readonly StringExLibrary Instance = new();

    public StringExLibrary()
    {
        Functions = [
            new("trim", Trim),
            new("trimStart", TrimStart),
            new("trimEnd", TrimEnd),
            new("lowerInvariant", LowerInvariant),
            new("upperInvariant", UpperInvariant),
            new("contains", Contains),
            new("startsWith", StartsWith),
            new("endsWith", EndsWith),
            new("equalsIgnoreCase", EqualsIgnoreCase),
        ];
    }

    public readonly LuaFunction[] Functions;

    public ValueTask<int> Trim(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        buffer.Span[0] = s.Trim();
        return new(1);
    }

    public ValueTask<int> TrimStart(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        buffer.Span[0] = s.TrimStart();
        return new(1);
    }

    public ValueTask<int> TrimEnd(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        buffer.Span[0] = s.TrimEnd();
        return new(1);
    }

    public ValueTask<int> LowerInvariant(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        buffer.Span[0] = s.ToLowerInvariant();
        return new(1);
    }

    public ValueTask<int> UpperInvariant(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        buffer.Span[0] = s.ToUpperInvariant();
        return new(1);
    }
    
    public ValueTask<int> Contains(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var s2 = context.GetArgument<string>(1);
        buffer.Span[0] = s.Contains(s2);
        return new(1);
    }
    
    public ValueTask<int> StartsWith(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var s2 = context.GetArgument<string>(1);
        buffer.Span[0] = s.StartsWith(s2);
        return new(1);
    }
    
    public ValueTask<int> EndsWith(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var s2 = context.GetArgument<string>(1);
        buffer.Span[0] = s.EndsWith(s2);
        return new(1);
    }

    public ValueTask<int> EqualsIgnoreCase(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var s2 = context.GetArgument<string>(1);
        buffer.Span[0] = string.Equals(s, s2, StringComparison.OrdinalIgnoreCase);
        return new(1);
    }
}