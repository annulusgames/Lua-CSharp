using System.Globalization;
using Lua.CodeAnalysis.Compilation;
using Lua.Internal;
using Lua.Runtime;

namespace Lua.Standard;

public sealed class BasicLibrary
{
    public static readonly BasicLibrary Instance = new();

    public BasicLibrary()
    {
        Functions = [
            new("assert", Assert),
            new("collectgarbage", CollectGarbage),
            new("dofile", DoFile),
            new("error", Error),
            new("getmetatable", GetMetatable),
            new("ipairs", IPairs),
            new("loadfile", LoadFile),
            new("load", Load),
            new("next", Next),
            new("pairs", Pairs),
            new("pcall", PCall),
            new("print", Print),
            new("rawequal", RawEqual),
            new("rawget", RawGet),
            new("rawlen", RawLen),
            new("rawset", RawSet),
            new("select", Select),
            new("setmetatable", SetMetatable),
            new("tonumber", ToNumber),
            new("tostring", ToString),
            new("type", Type),
            new("xpcall", XPCall),
        ];

        IPairsIterator = new("iterator", (context, buffer, cancellationToken) =>
        {
            var table = context.GetArgument<LuaTable>(0);
            var i = context.GetArgument<double>(1);

            i++;
            if (table.TryGetValue(i, out var value))
            {
                buffer.Span[0] = i;
                buffer.Span[1] = value;
            }
            else
            {
                buffer.Span[0] = LuaValue.Nil;
                buffer.Span[1] = LuaValue.Nil;
            }

            return new(2);
        });

        PairsIterator = new("iterator", Next);
    }

    public readonly LuaFunction[] Functions;
    readonly LuaFunction IPairsIterator;
    readonly LuaFunction PairsIterator;

    public ValueTask<int> Assert(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (!arg0.ToBoolean())
        {
            var message = "assertion failed!";
            if (context.HasArgument(1))
            {
                message = context.GetArgument<string>(1);
            }

            throw new LuaAssertionException(context.State.GetTraceback(), message);
        }

        context.Arguments.CopyTo(buffer.Span);
        return new(context.ArgumentCount);
    }

    public ValueTask<int> CollectGarbage(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        GC.Collect();
        return new(0);
    }

    public async ValueTask<int> DoFile(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<string>(0);

        // do not use LuaState.DoFileAsync as it uses the newExecutionContext
        var text = await File.ReadAllTextAsync(arg0, cancellationToken);
        var fileName = Path.GetFileName(arg0);
        var chunk = LuaCompiler.Default.Compile(text, "@"+fileName);

        return await new Closure(context.State, chunk).InvokeAsync(context, buffer, cancellationToken);
    }

    public ValueTask<int> Error(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var value = context.ArgumentCount == 0 || context.Arguments[0].Type is LuaValueType.Nil
            ? "(error object is a nil value)"
            : context.Arguments[0];

        Traceback t;
        try
        {
           t = context.State.GetTraceback(context.Thread);
            
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        throw new LuaRuntimeException(t, value);
    }

    public ValueTask<int> GetMetatable(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (arg0.TryRead<LuaTable>(out var table))
        {
            if (table.Metatable == null)
            {
                buffer.Span[0] = LuaValue.Nil;
            }
            else if (table.Metatable.TryGetValue(Metamethods.Metatable, out var metatable))
            {
                buffer.Span[0] = metatable;
            }
            else
            {
                buffer.Span[0] = table.Metatable;
            }
        }
        else
        {
            buffer.Span[0] = LuaValue.Nil;
        }

        return new(1);
    }

    public ValueTask<int> IPairs(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);

        // If table has a metamethod __ipairs, calls it with table as argument and returns the first three results from the call.
        if (arg0.Metatable != null && arg0.Metatable.TryGetValue(Metamethods.IPairs, out var metamethod))
        {
            if (!metamethod.TryRead<LuaFunction>(out var function))
            {
                LuaRuntimeException.AttemptInvalidOperation(context.State.GetTraceback(), "call", metamethod);
            }

            return function.InvokeAsync(context, buffer, cancellationToken);
        }

        buffer.Span[0] = IPairsIterator;
        buffer.Span[1] = arg0;
        buffer.Span[2] = 0;
        return new(3);
    }

    public async ValueTask<int> LoadFile(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        // Lua-CSharp does not support binary chunks, the mode argument is ignored.
        var arg0 = context.GetArgument<string>(0);
        var arg2 = context.HasArgument(2)
            ? context.GetArgument<LuaTable>(2)
            : null;

        // do not use LuaState.DoFileAsync as it uses the newExecutionContext
        try
        {
            var text = await File.ReadAllTextAsync(arg0, cancellationToken);
            var fileName = Path.GetFileName(arg0);
            var chunk = LuaCompiler.Default.Compile(text, fileName);
            buffer.Span[0] = new Closure(context.State, chunk, arg2);
            return 1;
        }
        catch (Exception ex)
        {
            buffer.Span[0] = LuaValue.Nil;
            buffer.Span[1] = ex.Message;
            return 2;
        }
    }

    public ValueTask<int> Load(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        // Lua-CSharp does not support binary chunks, the mode argument is ignored.
        var arg0 = context.GetArgument(0);

        var arg1 = context.HasArgument(1)
            ? context.GetArgument<string>(1)
            : null;

        var arg3 = context.HasArgument(3)
            ? context.GetArgument<LuaTable>(3)
            : null;

        // do not use LuaState.DoFileAsync as it uses the newExecutionContext
        try
        {
            if (arg0.TryRead<string>(out var str))
            {
                var chunk = LuaCompiler.Default.Compile(str, arg1 ?? str);
                buffer.Span[0] = new Closure(context.State, chunk, arg3);
                return new(1);
            }
            else if (arg0.TryRead<LuaFunction>(out var function))
            {
                // TODO: 
                throw new NotImplementedException();
            }
            else
            {
                LuaRuntimeException.BadArgument(context.State.GetTraceback(), 1, "load");
                return default; // dummy
            }
        }
        catch (Exception ex)
        {
            buffer.Span[0] = LuaValue.Nil;
            buffer.Span[1] = ex.Message;
            return new(2);
        }
    }

    public ValueTask<int> Next(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.HasArgument(1) ? context.Arguments[1] : LuaValue.Nil;

        if (arg0.TryGetNext(arg1, out var kv))
        {
            buffer.Span[0] = kv.Key;
            buffer.Span[1] = kv.Value;
            return new(2);
        }
        else
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }
    }

    public ValueTask<int> Pairs(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);

        // If table has a metamethod __pairs, calls it with table as argument and returns the first three results from the call.
        if (arg0.Metatable != null && arg0.Metatable.TryGetValue(Metamethods.Pairs, out var metamethod))
        {
            if (!metamethod.TryRead<LuaFunction>(out var function))
            {
                LuaRuntimeException.AttemptInvalidOperation(context.State.GetTraceback(), "call", metamethod);
            }

            return function.InvokeAsync(context, buffer, cancellationToken);
        }

        buffer.Span[0] = PairsIterator;
        buffer.Span[1] = arg0;
        buffer.Span[2] = LuaValue.Nil;
        return new(3);
    }

    public async ValueTask<int> PCall(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaFunction>(0);

        try
        {
            using var methodBuffer = new PooledArray<LuaValue>(1024);

            var resultCount = await arg0.InvokeAsync(context with
            {
                State = context.State,
                ArgumentCount = context.ArgumentCount - 1,
                FrameBase = context.FrameBase + 1,
            }, methodBuffer.AsMemory(), cancellationToken);

            buffer.Span[0] = true;
            methodBuffer.AsSpan()[..resultCount].CopyTo(buffer.Span[1..]);

            return resultCount + 1;
        }
        catch (Exception ex)
        {
            buffer.Span[0] = false;
            if (ex is LuaRuntimeException { ErrorObject: not null } luaEx)
            {
                buffer.Span[1] = luaEx.ErrorObject.Value;
            }
            else
            {
                buffer.Span[1] = ex.Message;
            }

            return 2;
        }
    }

    public async ValueTask<int> Print(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        using var methodBuffer = new PooledArray<LuaValue>(1);

        for (int i = 0; i < context.ArgumentCount; i++)
        {
            await context.Arguments[i].CallToStringAsync(context, methodBuffer.AsMemory(), cancellationToken);
            Console.Write(methodBuffer[0]);
            Console.Write('\t');
        }

        Console.WriteLine();
        return 0;
    }

    public ValueTask<int> RawEqual(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);
        var arg1 = context.GetArgument(1);

        buffer.Span[0] = arg0 == arg1;
        return new(1);
    }

    public ValueTask<int> RawGet(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.GetArgument(1);

        buffer.Span[0] = arg0[arg1];
        return new(1);
    }

    public ValueTask<int> RawLen(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (arg0.TryRead<LuaTable>(out var table))
        {
            buffer.Span[0] = table.ArrayLength;
        }
        else if (arg0.TryRead<string>(out var str))
        {
            buffer.Span[0] = str.Length;
        }
        else
        {
            LuaRuntimeException.BadArgument(context.State.GetTraceback(), 2, "rawlen", [LuaValueType.String, LuaValueType.Table]);
        }

        return new(1);
    }

    public ValueTask<int> RawSet(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.GetArgument(1);
        var arg2 = context.GetArgument(2);

        arg0[arg1] = arg2;
        return new(0);
    }

    public ValueTask<int> Select(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (arg0.TryRead<int>(out var index))
        {
            if (Math.Abs(index) > context.ArgumentCount)
            {
                throw new LuaRuntimeException(context.State.GetTraceback(), "bad argument #1 to 'select' (index out of range)");
            }

            var span = index >= 0
                ? context.Arguments[index..]
                : context.Arguments[(context.ArgumentCount + index)..];

            span.CopyTo(buffer.Span);

            return new(span.Length);
        }
        else if (arg0.TryRead<string>(out var str) && str == "#")
        {
            buffer.Span[0] = context.ArgumentCount - 1;
            return new(1);
        }
        else
        {
            LuaRuntimeException.BadArgument(context.State.GetTraceback(), 1, "select", "number", arg0.Type.ToString());
            return default;
        }
    }

    public ValueTask<int> SetMetatable(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.GetArgument(1);

        if (arg1.Type is not (LuaValueType.Nil or LuaValueType.Table))
        {
            LuaRuntimeException.BadArgument(context.State.GetTraceback(), 2, "setmetatable", [LuaValueType.Nil, LuaValueType.Table]);
        }

        if (arg0.Metatable != null && arg0.Metatable.TryGetValue(Metamethods.Metatable, out _))
        {
            throw new LuaRuntimeException(context.State.GetTraceback(), "cannot change a protected metatable");
        }
        else if (arg1.Type is LuaValueType.Nil)
        {
            arg0.Metatable = null;
        }
        else
        {
            arg0.Metatable = arg1.Read<LuaTable>();
        }

        buffer.Span[0] = arg0;
        return new(1);
    }

    public ValueTask<int> ToNumber(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var e = context.GetArgument(0);
        int? toBase = context.HasArgument(1)
            ? (int)context.GetArgument<double>(1)
            : null;

        if (toBase != null && (toBase < 2 || toBase > 36))
        {
            throw new LuaRuntimeException(context.State.GetTraceback(), "bad argument #2 to 'tonumber' (base out of range)");
        }

        double? value = null;
        if (e.Type is LuaValueType.Number)
        {
            value = e.UnsafeRead<double>();
        }
        else if (e.TryRead<string>(out var str))
        {
            if (toBase == null)
            {
                if (e.TryRead<double>(out var result))
                {
                    value = result;
                }
            }
            else if (toBase == 10)
            {
                if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                {
                    value = result;
                }
            }
            else
            {
                try
                {
                    // if the base is not 10, str cannot contain a minus sign
                    var span = str.AsSpan().Trim();
                    if (span.Length == 0) goto END;

                    var first = span[0];
                    var sign = first == '-' ? -1 : 1;
                    if (first is '+' or '-')
                    {
                        span = span[1..];
                    }
                    if (span.Length == 0) goto END;

                    if (toBase == 16 && span.Length > 2 && span[0] is '0' && span[1] is 'x' or 'X')
                    {
                        value = sign * HexConverter.ToDouble(span);
                    }
                    else
                    {
                        value = sign * StringToDouble(span, toBase.Value);
                    }
                }
                catch (FormatException)
                {
                    goto END;
                }
            }
        }
        else
        {
            goto END;
        }

    END:
        if (value != null && double.IsNaN(value.Value))
        {
            value = null;
        }

        buffer.Span[0] = value == null ? LuaValue.Nil : value.Value;
        return new(1);
    }

    static double StringToDouble(ReadOnlySpan<char> text, int toBase)
    {
        var value = 0.0;
        for (int i = 0; i < text.Length; i++)
        {
            var v = text[i] switch
            {
                '0' => 0,
                '1' => 1,
                '2' => 2,
                '3' => 3,
                '4' => 4,
                '5' => 5,
                '6' => 6,
                '7' => 7,
                '8' => 8,
                '9' => 9,
                'a' or 'A' => 10,
                'b' or 'B' => 11,
                'c' or 'C' => 12,
                'd' or 'D' => 13,
                'e' or 'E' => 14,
                'f' or 'F' => 15,
                'g' or 'G' => 16,
                'h' or 'H' => 17,
                'i' or 'I' => 18,
                'j' or 'J' => 19,
                'k' or 'K' => 20,
                'l' or 'L' => 21,
                'm' or 'M' => 22,
                'n' or 'N' => 23,
                'o' or 'O' => 24,
                'p' or 'P' => 25,
                'q' or 'Q' => 26,
                'r' or 'R' => 27,
                's' or 'S' => 28,
                't' or 'T' => 29,
                'u' or 'U' => 30,
                'v' or 'V' => 31,
                'w' or 'W' => 32,
                'x' or 'X' => 33,
                'y' or 'Y' => 34,
                'z' or 'Z' => 35,
                _ => 0,
            };

            if (v >= toBase)
            {
                throw new FormatException();
            }

            value += v * Math.Pow(toBase, text.Length - i - 1);
        }

        return value;
    }

    public ValueTask<int> ToString(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);
        return arg0.CallToStringAsync(context, buffer, cancellationToken);
    }

    public ValueTask<int> Type(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        buffer.Span[0] = arg0.Type switch
        {
            LuaValueType.Nil => "nil",
            LuaValueType.Boolean => "boolean",
            LuaValueType.String => "string",
            LuaValueType.Number => "number",
            LuaValueType.Function => "function",
            LuaValueType.Thread => "thread",
            LuaValueType.UserData => "userdata",
            LuaValueType.Table => "table",
            _ => throw new NotImplementedException(),
        };

        return new(1);
    }

    public async ValueTask<int> XPCall(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaFunction>(0);
        var arg1 = context.GetArgument<LuaFunction>(1);

        using var methodBuffer = new PooledArray<LuaValue>(1024);
        methodBuffer.AsSpan().Clear();

        try
        {
            var resultCount = await arg0.InvokeAsync(context with
            {
                State = context.State,
                ArgumentCount = context.ArgumentCount - 2,
                FrameBase = context.FrameBase + 2,
            }, methodBuffer.AsMemory(), cancellationToken);

            buffer.Span[0] = true;
            methodBuffer.AsSpan()[..resultCount].CopyTo(buffer.Span[1..]);

            return resultCount + 1;
        }
        catch (Exception ex)
        {
            methodBuffer.AsSpan().Clear();
            var error = ex is LuaRuntimeException { ErrorObject: not null } luaEx ? luaEx.ErrorObject.Value : ex.Message;

            context.State.Push(error);

            // invoke error handler
            await arg1.InvokeAsync(context with
            {
                State = context.State,
                ArgumentCount = 1,
                FrameBase = context.Thread.Stack.Count - 1,
            }, methodBuffer.AsMemory(), cancellationToken);

            buffer.Span[0] = false;
            buffer.Span[1] = methodBuffer[0];


            return 2;
        }
    }
}