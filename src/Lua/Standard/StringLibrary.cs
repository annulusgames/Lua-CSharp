using System.Text;
using System.Text.RegularExpressions;
using Lua.Internal;
using Lua.Runtime;

namespace Lua.Standard;

public sealed class StringLibrary
{
    public static readonly StringLibrary Instance = new();

    public StringLibrary()
    {
        Functions = [
            new("byte", Byte),
            new("char", Char),
            new("dump", Dump),
            new("find", Find),
            new("format", Format),
            new("gmatch", GMatch),
            new("gsub", GSub),
            new("len", Len),
            new("lower", Lower),
            new("rep", Rep),
            new("reverse", Reverse),
            new("sub", Sub),
            new("upper", Upper),
        ];
    }

    public readonly LuaFunction[] Functions;

    public ValueTask<int> Byte(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var i = context.HasArgument(1)
            ? context.GetArgument<double>(1)
            : 1;
        var j = context.HasArgument(2)
            ? context.GetArgument<double>(2)
            : i;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, "byte", 2, i);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, "byte", 3, j);

        var span = StringHelper.Slice(s, (int)i, (int)j);
        for (int k = 0; k < span.Length; k++)
        {
            buffer.Span[k] = span[k];
        }

        return new(span.Length);
    }

    public ValueTask<int> Char(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        if (context.ArgumentCount == 0)
        {
            buffer.Span[0] = "";
            return new(1);
        }

        var builder = new ValueStringBuilder(context.ArgumentCount);
        for (int i = 0; i < context.ArgumentCount; i++)
        {
            var arg = context.GetArgument<double>(i);
            LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, "char", i + 1, arg);
            builder.Append((char)arg);
        }

        buffer.Span[0] = builder.ToString();
        return new(1);
    }

    public ValueTask<int> Dump(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        // stirng.dump is not supported (throw exception)
        throw new NotSupportedException("stirng.dump is not supported");
    }

    public ValueTask<int> Find(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var pattern = context.GetArgument<string>(1);
        var init = context.HasArgument(2)
            ? context.GetArgument<double>(2)
            : 1;
        var plain = context.HasArgument(3)
            ? context.GetArgument(3).ToBoolean()
            : false;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, "find", 3, init);

        // init can be negative value
        if (init < 0)
        {
            init = s.Length + init + 1;
        }

        // out of range
        if (init != 1 && (init < 1 || init > s.Length))
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }

        // empty pattern
        if (pattern.Length == 0)
        {
            buffer.Span[0] = 1;
            buffer.Span[1] = 1;
            return new(2);
        }

        var source = s.AsSpan()[(int)(init - 1)..];

        if (plain)
        {
            var start = source.IndexOf(pattern);
            if (start == -1)
            {
                buffer.Span[0] = LuaValue.Nil;
                return new(1);
            }

            // 1-based
            buffer.Span[0] = start + 1;
            buffer.Span[1] = start + pattern.Length;
            return new(2);
        }
        else
        {
            var regex = StringHelper.ToRegex(pattern);
            var match = regex.Match(source.ToString());

            if (match.Success)
            {
                // 1-based
                buffer.Span[0] = init + match.Index;
                buffer.Span[1] = init + match.Index + match.Length - 1;
                return new(2);
            }
            else
            {
                buffer.Span[0] = LuaValue.Nil;
                return new(1);
            }
        }
    }

    public async ValueTask<int> Format(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var format = context.GetArgument<string>(0);

        // TODO: pooling StringBuilder
        var builder = new StringBuilder(format.Length * 2);
        var parameterIndex = 1;

        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] == '%')
            {
                i++;

                // escape
                if (format[i] == '%')
                {
                    builder.Append('%');
                    continue;
                }

                var leftJustify = false;
                var plusSign = false;
                var zeroPadding = false;
                var alternateForm = false;
                var blank = false;
                var width = 0;
                var precision = -1;

                // Process flags
                while (true)
                {
                    var c = format[i];
                    switch (c)
                    {
                        case '-':
                            if (leftJustify) throw new LuaRuntimeException(context.State.GetTraceback(), "invalid format (repeated flags)");
                            leftJustify = true;
                            break;
                        case '+':
                            if (plusSign) throw new LuaRuntimeException(context.State.GetTraceback(), "invalid format (repeated flags)");
                            plusSign = true;
                            break;
                        case '0':
                            if (zeroPadding) throw new LuaRuntimeException(context.State.GetTraceback(), "invalid format (repeated flags)");
                            zeroPadding = true;
                            break;
                        case '#':
                            if (alternateForm) throw new LuaRuntimeException(context.State.GetTraceback(), "invalid format (repeated flags)");
                            alternateForm = true;
                            break;
                        case ' ':
                            if (blank) throw new LuaRuntimeException(context.State.GetTraceback(), "invalid format (repeated flags)");
                            blank = true;
                            break;
                        default:
                            goto PROCESS_WIDTH;
                    }

                    i++;
                }

            PROCESS_WIDTH:

                // Process width
                var start = i;
                if (char.IsDigit(format[i]))
                {
                    i++;
                    if (char.IsDigit(format[i])) i++;
                    if (char.IsDigit(format[i])) throw new LuaRuntimeException(context.State.GetTraceback(), "invalid format (width or precision too long)");
                    width = int.Parse(format.AsSpan()[start..i]);
                }

                // Process precision
                if (format[i] == '.')
                {
                    i++;
                    start = i;
                    if (char.IsDigit(format[i])) i++;
                    if (char.IsDigit(format[i])) i++;
                    if (char.IsDigit(format[i])) throw new LuaRuntimeException(context.State.GetTraceback(), "invalid format (width or precision too long)");
                    precision = int.Parse(format.AsSpan()[start..i]);
                }

                // Process conversion specifier
                var specifier = format[i];

                if (context.ArgumentCount <= parameterIndex)
                {
                    throw new LuaRuntimeException(context.State.GetTraceback(), $"bad argument #{parameterIndex + 1} to 'format' (no value)");
                }
                var parameter = context.GetArgument(parameterIndex++);

                // TODO: reduce allocation
                string formattedValue = default!;
                switch (specifier)
                {
                    case 'f':
                    case 'e':
                    case 'g':
                    case 'G':
                        if (!parameter.TryRead<double>(out var f))
                        {
                            LuaRuntimeException.BadArgument(context.State.GetTraceback(), parameterIndex + 1, "format", LuaValueType.Number.ToString(), parameter.Type.ToString());
                        }

                        switch (specifier)
                        {
                            case 'f':
                                formattedValue = precision < 0
                                    ? f.ToString()
                                    : f.ToString($"F{precision}");
                                break;
                            case 'e':
                                formattedValue = precision < 0
                                    ? f.ToString()
                                    : f.ToString($"E{precision}");
                                break;
                            case 'g':
                                formattedValue = precision < 0
                                    ? f.ToString()
                                    : f.ToString($"G{precision}");
                                break;
                            case 'G':
                                formattedValue = precision < 0
                                    ? f.ToString().ToUpper()
                                    : f.ToString($"G{precision}").ToUpper();
                                break;
                        }

                        if (plusSign && f >= 0)
                        {
                            formattedValue = $"+{formattedValue}";
                        }
                        break;
                    case 's':
                        using (var strBuffer = new PooledArray<LuaValue>(1))
                        {
                            await parameter.CallToStringAsync(context, strBuffer.AsMemory(), cancellationToken);
                            formattedValue = strBuffer[0].Read<string>();
                        }

                        if (specifier is 's' && precision > 0 && precision <= formattedValue.Length)
                        {
                            formattedValue = formattedValue[..precision];
                        }
                        break;
                    case 'q':
                        switch (parameter.Type)
                        {
                            case LuaValueType.Nil:
                                formattedValue = "nil";
                                break;
                            case LuaValueType.Boolean:
                                formattedValue = parameter.Read<bool>() ? "true" : "false";
                                break;
                            case LuaValueType.String:
                                formattedValue = $"\"{StringHelper.Escape(parameter.Read<string>())}\"";
                                break;
                            case LuaValueType.Number:
                                // TODO: floating point numbers must be in hexadecimal notation
                                formattedValue = parameter.Read<double>().ToString();
                                break;
                            default:
                                using (var strBuffer = new PooledArray<LuaValue>(1))
                                {
                                    await parameter.CallToStringAsync(context, strBuffer.AsMemory(), cancellationToken);
                                    formattedValue = strBuffer[0].Read<string>();
                                }
                                break;
                        }
                        break;
                    case 'i':
                    case 'd':
                    case 'u':
                    case 'c':
                    case 'x':
                    case 'X':
                        if (!parameter.TryRead<double>(out var x))
                        {
                            LuaRuntimeException.BadArgument(context.State.GetTraceback(), parameterIndex + 1, "format", LuaValueType.Number.ToString(), parameter.Type.ToString());
                        }

                        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, "format", parameterIndex + 1, x);

                        switch (specifier)
                        {
                            case 'i':
                            case 'd':
                                {
                                    var integer = checked((long)x);
                                    formattedValue = precision < 0
                                        ? integer.ToString()
                                        : integer.ToString($"D{precision}");
                                }
                                break;
                            case 'u':
                                {
                                    var integer = checked((ulong)x);
                                    formattedValue = precision < 0
                                        ? integer.ToString()
                                        : integer.ToString($"D{precision}");
                                }
                                break;
                            case 'c':
                                formattedValue = ((char)(int)x).ToString();
                                break;
                            case 'x':
                                {
                                    var integer = checked((ulong)x);
                                    formattedValue = alternateForm
                                        ? $"0x{integer:x}"
                                        : $"{integer:x}";
                                }
                                break;
                            case 'X':
                                {
                                    var integer = checked((ulong)x);
                                    formattedValue = alternateForm
                                        ? $"0X{integer:X}"
                                        : $"{integer:X}";
                                }
                                break;
                            case 'o':
                                {
                                    var integer = checked((long)x);
                                    formattedValue = Convert.ToString(integer, 8);
                                }
                                break;
                        }

                        if (plusSign && x >= 0)
                        {
                            formattedValue = $"+{formattedValue}";
                        }
                        break;
                    default:
                        throw new LuaRuntimeException(context.State.GetTraceback(), $"invalid option '%{specifier}' to 'format'");
                }

                // Apply blank (' ') flag for positive numbers
                if (specifier is 'd' or 'i' or 'f' or 'g' or 'G')
                {
                    if (blank && !leftJustify && !zeroPadding && parameter.Read<double>() >= 0)
                    {
                        formattedValue = $" {formattedValue}";
                    }
                }

                // Apply width and padding
                if (width > formattedValue.Length)
                {
                    if (leftJustify)
                    {
                        formattedValue = formattedValue.PadRight(width);
                    }
                    else
                    {
                        formattedValue = zeroPadding ? formattedValue.PadLeft(width, '0') : formattedValue.PadLeft(width);
                    }
                }

                builder.Append(formattedValue);
            }
            else
            {
                builder.Append(format[i]);
            }
        }


        buffer.Span[0] = builder.ToString();
        return 1;
    }

    public ValueTask<int> GMatch(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var pattern = context.GetArgument<string>(1);

        var regex = StringHelper.ToRegex(pattern);
        var matches = regex.Matches(s);

        buffer.Span[0] = new CsClosure("iterator",[new LuaValue(matches),0],static (context, buffer, cancellationToken) =>
        {
            var upValues = context.GetCsClosure()!.UpValues;
            var matches = upValues[0].Read<MatchCollection>();
            var i = upValues[1].Read<int>();
            if (matches.Count > i)
            {
                var match = matches[i];
                var groups = match.Groups;

                i++;
                 upValues[1] = i;
                if (groups.Count == 1)
                {
                    buffer.Span[0] = match.Value;
                }
                else
                {
                    for (int j = 0; j < groups.Count; j++)
                    {
                        buffer.Span[j] = groups[j + 1].Value;
                    }
                }

                return new(groups.Count);
            }
            else
            {
                buffer.Span[0] = LuaValue.Nil;
                return new(1);
            }
        });

        return new(1);
    }

    public async ValueTask<int> GSub(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var pattern = context.GetArgument<string>(1);
        var repl = context.GetArgument(2);
        var n_arg = context.HasArgument(3)
            ? context.GetArgument<double>(3)
            : int.MaxValue;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, "gsub", 4, n_arg);

        var n = (int)n_arg;
        var regex = StringHelper.ToRegex(pattern);
        var matches = regex.Matches(s);

        // TODO: reduce allocation
        var builder = new StringBuilder();
        var lastIndex = 0;
        var replaceCount = 0;

        for (int i = 0; i < matches.Count; i++)
        {
            if (replaceCount > n) break;

            var match = matches[i];
            builder.Append(s.AsSpan()[lastIndex..match.Index]);
            replaceCount++;

            LuaValue result;
            if (repl.TryRead<string>(out var str))
            {
                result = str.Replace("%%", "%")
                    .Replace("%0", match.Value);

                for (int k = 1; k <= match.Groups.Count; k++)
                {
                    if (replaceCount > n) break;
                    result = result.Read<string>().Replace($"%{k}", match.Groups[k].Value);
                    replaceCount++;
                }
            }
            else if (repl.TryRead<LuaTable>(out var table))
            {
                result = table[match.Groups[1].Value];
            }
            else if (repl.TryRead<LuaFunction>(out var func))
            {
                for (int k = 1; k <= match.Groups.Count; k++)
                {
                    context.State.Push(match.Groups[k].Value);
                }

                using var methodBuffer = new PooledArray<LuaValue>(1024);
                await func.InvokeAsync(context with
                {
                    ArgumentCount = match.Groups.Count,
                    FrameBase = context.Thread.Stack.Count - context.ArgumentCount,
                }, methodBuffer.AsMemory(), cancellationToken);

                result = methodBuffer[0];
            }
            else
            {
                throw new LuaRuntimeException(context.State.GetTraceback(), "bad argument #3 to 'gsub' (string/function/table expected)");
            }

            if (result.TryRead<string>(out var rs))
            {
                builder.Append(rs);
            }
            else if (result.TryRead<double>(out var rd))
            {
                builder.Append(rd);
            }
            else if (!result.ToBoolean())
            {
                builder.Append(match.Value);
                replaceCount--;
            }
            else
            {
                throw new LuaRuntimeException(context.State.GetTraceback(), $"invalid replacement value (a {result.Type})");
            }

            lastIndex = match.Index + match.Length;
        }

        builder.Append(s.AsSpan()[lastIndex..s.Length]);

        buffer.Span[0] = builder.ToString();
        return 1;
    }

    public ValueTask<int> Len(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        buffer.Span[0] = s.Length;
        return new(1);
    }

    public ValueTask<int> Lower(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        buffer.Span[0] = s.ToLower();
        return new(1);
    }

    public ValueTask<int> Rep(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var n_arg = context.GetArgument<double>(1);
        var sep = context.HasArgument(2)
            ? context.GetArgument<string>(2)
            : null;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, "rep", 2, n_arg);

        var n = (int)n_arg;

        var builder = new ValueStringBuilder(s.Length * n);
        for (int i = 0; i < n; i++)
        {
            builder.Append(s);
            if (i != n - 1 && sep != null)
            {
                builder.Append(sep);
            }
        }

        buffer.Span[0] = builder.ToString();
        return new(1);
    }

    public ValueTask<int> Reverse(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        using var strBuffer = new PooledArray<char>(s.Length);
        var span = strBuffer.AsSpan()[..s.Length];
        s.AsSpan().CopyTo(span);
        span.Reverse();
        buffer.Span[0] = span.ToString();
        return new(1);
    }

    public ValueTask<int> Sub(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var i = context.GetArgument<double>(1);
        var j = context.HasArgument(2)
            ? context.GetArgument<double>(2)
            : -1;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, "sub", 2, i);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, "sub", 3, j);

        buffer.Span[0] = StringHelper.Slice(s, (int)i, (int)j).ToString();
        return new(1);
    }

    public ValueTask<int> Upper(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        buffer.Span[0] = s.ToUpper();
        return new(1);
    }
}