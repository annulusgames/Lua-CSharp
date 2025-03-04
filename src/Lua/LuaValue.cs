using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public enum LuaValueType : byte
{
    Nil,
    Boolean,
    String,
    Number,
    Function,
    Thread,
    LightUserData,
    UserData,
    Table,
}

[StructLayout(LayoutKind.Auto)]
public readonly struct LuaValue : IEquatable<LuaValue>
{
    public static readonly LuaValue Nil = default;

    public readonly LuaValueType Type;
    readonly double value;
    readonly object? referenceValue;

    public bool TryRead<T>(out T result)
    {
        var t = typeof(T);

        switch (Type)
        {
            case LuaValueType.Number:
                if (t == typeof(float))
                {
                    var v = (float)value;
                    result = Unsafe.As<float, T>(ref v);
                    return true;
                }
                else if (t == typeof(double))
                {
                    var v = value;
                    result = Unsafe.As<double, T>(ref v);
                    return true;
                }
                else if (t == typeof(int))
                {
                    if (!MathEx.IsInteger(value)) break;
                    var v = (int)value;
                    result = Unsafe.As<int, T>(ref v);
                    return true;
                }
                else if (t == typeof(long))
                {
                    if (!MathEx.IsInteger(value)) break;
                    var v = (long)value;
                    result = Unsafe.As<long, T>(ref v);
                    return true;
                }
                else if (t == typeof(object))
                {
                    result = (T)(object)value;
                    return true;
                }
                else
                {
                    break;
                }
            case LuaValueType.Boolean:
                if (t == typeof(bool))
                {
                    var v = value != 0;
                    result = Unsafe.As<bool, T>(ref v);
                    return true;
                }
                else if (t == typeof(object))
                {
                    result = (T)(object)value;
                    return true;
                }
                else
                {
                    break;
                }
            case LuaValueType.String:
                if (t == typeof(string))
                {
                    var v = referenceValue!;
                    result = Unsafe.As<object, T>(ref v);
                    return true;
                }
                else if (t == typeof(double))
                {
                    result = default!;
                    return TryParseToDouble(out Unsafe.As<T, double>(ref result));
                }
                else if (t == typeof(object))
                {
                    result = (T)referenceValue!;
                    return true;
                }
                else
                {
                    break;
                }
            case LuaValueType.Function:
                if (t == typeof(LuaFunction) || t.IsSubclassOf(typeof(LuaFunction)))
                {
                    var v = referenceValue!;
                    result = Unsafe.As<object, T>(ref v);
                    return true;
                }
                else if (t == typeof(object))
                {
                    result = (T)referenceValue!;
                    return true;
                }
                else
                {
                    break;
                }
            case LuaValueType.Thread:
                if (t == typeof(LuaThread))
                {
                    var v = referenceValue!;
                    result = Unsafe.As<object, T>(ref v);
                    return true;
                }
                else if (t == typeof(object))
                {
                    result = (T)referenceValue!;
                    return true;
                }
                else
                {
                    break;
                }
            case LuaValueType.LightUserData:
                {
                    if (referenceValue is T tValue)
                    {
                        result = tValue;
                        return true;
                    }

                    break;
                }
            case LuaValueType.UserData:
                if (t == typeof(ILuaUserData) || typeof(ILuaUserData).IsAssignableFrom(t))
                {
                    if (referenceValue is T tValue)
                    {
                        result = tValue;
                        return true;
                    }

                    break;
                }
                else if (t == typeof(object))
                {
                    result = (T)referenceValue!;
                    return true;
                }
                else
                {
                    break;
                }
            case LuaValueType.Table:
                if (t == typeof(LuaTable))
                {
                    var v = referenceValue!;
                    result = Unsafe.As<object, T>(ref v);
                    return true;
                }
                else if (t == typeof(object))
                {
                    result = (T)referenceValue!;
                    return true;
                }
                else
                {
                    break;
                }
        }

        result = default!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReadBool(out bool result)
    {
        if (Type == LuaValueType.Boolean)
        {
            result = value != 0;
            return true;
        }

        result = default!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReadNumber(out double result)
    {
        if (Type == LuaValueType.Number)
        {
            result = value;
            return true;
        }

        result = default!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReadTable(out LuaTable result)
    {
        if (Type == LuaValueType.Table)
        {
            var v = referenceValue!;
            result = Unsafe.As<object, LuaTable>(ref v);
            return true;
        }

        result = default!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReadFunction(out LuaFunction result)
    {
        if (Type == LuaValueType.Function)
        {
            var v = referenceValue!;
            result = Unsafe.As<object, LuaFunction>(ref v);
            return true;
        }

        result = default!;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReadString(out string result)
    {
        if (Type == LuaValueType.String)
        {
            var v = referenceValue!;
            result = Unsafe.As<object, string>(ref v);
            return true;
        }

        result = default!;
        return false;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryReadDouble(out double result)
    {
        if (Type == LuaValueType.Number)
        {
            result = value;
            return true;
        }

        return TryParseToDouble(out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryReadOrSetDouble(ref LuaValue luaValue, out double result)
    {
        if (luaValue.Type == LuaValueType.Number)
        {
            result = luaValue.value;
            return true;
        }

        if (luaValue.TryParseToDouble(out result))
        {
            luaValue = result;
            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal double UnsafeReadDouble()
    {
        return value;
    }

    bool TryParseToDouble(out double result)
    {
        if (Type != LuaValueType.String)
        {
            result = default!;
            return false;
        }

        var str = Unsafe.As<string>(referenceValue!);
        var span = str.AsSpan().Trim();
        if (span.Length == 0)
        {
            result = default!;
            return false;
        }

        var sign = 1;
        var first = span[0];
        if (first is '+')
        {
            sign = 1;
            span = span[1..];
        }
        else if (first is '-')
        {
            sign = -1;
            span = span[1..];
        }

        if (span.Length > 2 && span[0] is '0' && span[1] is 'x' or 'X')
        {
            // TODO: optimize
            try
            {
                var d = HexConverter.ToDouble(span) * sign;
                result = d;
                return true;
            }
            catch (FormatException)
            {
                result = default!;
                return false;
            }
        }
        else
        {
            return double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }
    }

    public T Read<T>()
    {
        if (!TryRead<T>(out var result)) throw new InvalidOperationException($"Cannot convert LuaValueType.{Type} to {typeof(T).FullName}.");
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal T UnsafeRead<T>()
    {
        switch (Type)
        {
            case LuaValueType.Boolean:
                {
                    var v = value != 0;
                    return Unsafe.As<bool, T>(ref v);
                }
            case LuaValueType.Number:
                {
                    var v = value;
                    return Unsafe.As<double, T>(ref v);
                }
            case LuaValueType.String:
            case LuaValueType.Thread:
            case LuaValueType.Function:
            case LuaValueType.Table:
            case LuaValueType.LightUserData:
            case LuaValueType.UserData:
                {
                    var v = referenceValue!;
                    return Unsafe.As<object, T>(ref v);
                }
        }

        return default!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ToBoolean()
    {
        if (Type == LuaValueType.Boolean) return value != 0;
        if (Type is LuaValueType.Nil) return false;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue(object obj)
    {
        Type = LuaValueType.LightUserData;
        referenceValue = obj;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue(bool value)
    {
        Type = LuaValueType.Boolean;
        this.value = value ? 1 : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue(double value)
    {
        Type = LuaValueType.Number;
        this.value = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue(string value)
    {
        Type = LuaValueType.String;
        referenceValue = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue(LuaFunction value)
    {
        Type = LuaValueType.Function;
        referenceValue = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue(LuaTable value)
    {
        Type = LuaValueType.Table;
        referenceValue = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue(LuaThread value)
    {
        Type = LuaValueType.Thread;
        referenceValue = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue(ILuaUserData value)
    {
        Type = LuaValueType.UserData;
        referenceValue = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator LuaValue(bool value)
    {
        return new(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator LuaValue(double value)
    {
        return new(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator LuaValue(string value)
    {
        return new(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator LuaValue(LuaTable value)
    {
        return new(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator LuaValue(LuaFunction value)
    {
        return new(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator LuaValue(LuaThread value)
    {
        return new(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        return Type switch
        {
            LuaValueType.Nil => 0,
            LuaValueType.Boolean or LuaValueType.Number => value.GetHashCode(),
            LuaValueType.String => Unsafe.As<string>(referenceValue)!.GetHashCode(),
            _ => referenceValue!.GetHashCode()
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(LuaValue other)
    {
        if (other.Type != Type) return false;

        return Type switch
        {
            LuaValueType.Nil => true,
            LuaValueType.Boolean or LuaValueType.Number => other.value == value,
            LuaValueType.String => Unsafe.As<string>(other.referenceValue) == Unsafe.As<string>(referenceValue),
            _ => other.referenceValue!.Equals(referenceValue)
        };
    }

    public override bool Equals(object? obj)
    {
        return obj is LuaValue value1 && Equals(value1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(LuaValue a, LuaValue b)
    {
        return a.Equals(b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(LuaValue a, LuaValue b)
    {
        return !a.Equals(b);
    }

    public override string ToString()
    {
        return Type switch
        {
            LuaValueType.Nil => "nil",
            LuaValueType.Boolean => Read<bool>() ? "true" : "false",
            LuaValueType.String => Read<string>(),
            LuaValueType.Number => Read<double>().ToString(CultureInfo.InvariantCulture),
            LuaValueType.Function => $"function: {referenceValue!.GetHashCode()}",
            LuaValueType.Thread => $"thread: {referenceValue!.GetHashCode()}",
            LuaValueType.Table => $"table: {referenceValue!.GetHashCode()}",
            LuaValueType.LightUserData => $"userdata: {referenceValue!.GetHashCode()}",
            LuaValueType.UserData => $"userdata: {referenceValue!.GetHashCode()}",
            _ => "",
        };
    }

    public static bool TryGetLuaValueType(Type type, out LuaValueType result)
    {
        if (type == typeof(double) || type == typeof(float) || type == typeof(int) || type == typeof(long))
        {
            result = LuaValueType.Number;
            return true;
        }
        else if (type == typeof(bool))
        {
            result = LuaValueType.Boolean;
            return true;
        }
        else if (type == typeof(string))
        {
            result = LuaValueType.String;
            return true;
        }
        else if (type == typeof(LuaFunction) || type.IsSubclassOf(typeof(LuaFunction)))
        {
            result = LuaValueType.Function;
            return true;
        }
        else if (type == typeof(LuaTable))
        {
            result = LuaValueType.Table;
            return true;
        }
        else if (type == typeof(LuaThread))
        {
            result = LuaValueType.Thread;
            return true;
        }
        else if (type == typeof(ILuaUserData) || type.IsAssignableFrom(typeof(ILuaUserData)))
        {
            result = LuaValueType.UserData;
            return true;
        }

        result = default;
        return false;
    }

    internal ValueTask<int> CallToStringAsync(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        if (this.TryGetMetamethod(context.State, Metamethods.ToString, out var metamethod))
        {
            if (!metamethod.TryReadFunction(out var func))
            {
                LuaRuntimeException.AttemptInvalidOperation(context.State.GetTraceback(), "call", metamethod);
            }

            context.State.Push(this);

            return func.InvokeAsync(context with
            {
                ArgumentCount = 1,
                FrameBase = context.Thread.Stack.Count - 1,
            }, buffer, cancellationToken);
        }
        else
        {
            buffer.Span[0] = ToString();
            return new(1);
        }
    }
}