
using System.Runtime.CompilerServices;

namespace Lua.Runtime;

public sealed class UpValue
{
    LuaValue value;

    public LuaThread Thread { get; }
    public bool IsClosed { get; private set; }
    public int RegisterIndex { get; private set; }

    UpValue(LuaThread thread)
    {
        Thread = thread;
    }

    public static UpValue Open(LuaThread thread, int registerIndex)
    {
        return new(thread)
        {
            RegisterIndex = registerIndex
        };
    }

    public static UpValue Closed(LuaThread thread, LuaValue value)
    {
        return new(thread)
        {
            IsClosed = true,
            value = value
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue GetValue()
    {
        if (IsClosed)
        {
            return value;
        }
        else
        {
            return Thread.Stack.UnsafeGet(RegisterIndex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValue(LuaValue value)
    {
        if (IsClosed)
        {
            this.value = value;
        }
        else
        {
            Thread.Stack.UnsafeGet(RegisterIndex) = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Close()
    {
        if (!IsClosed)
        {
            value = Thread.Stack.UnsafeGet(RegisterIndex);
        }

        IsClosed = true;
    }
}