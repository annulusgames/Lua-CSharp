using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lua.CodeAnalysis;
using Lua.Runtime;

namespace Lua;

[StructLayout(LayoutKind.Auto)]
public readonly record struct LuaFunctionExecutionContext
{
    public required LuaState State { get; init; }
    public required LuaThread Thread { get; init; }
    public required int ArgumentCount { get; init; }
    public required int FrameBase { get; init; }
    public SourcePosition? SourcePosition { get; init; }
    public string? RootChunkName { get; init; }
    public string? ChunkName { get; init; }
    public int? CallerInstructionIndex { get; init; }
    public object? AdditionalContext { get; init; }

    public ReadOnlySpan<LuaValue> Arguments
    {
        get { return Thread.GetStackValues().Slice(FrameBase, ArgumentCount); }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasArgument(int index)
    {
        return ArgumentCount > index && Arguments[index].Type is not LuaValueType.Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue GetArgument(int index)
    {
        ThrowIfArgumentNotExists(index);
        return Arguments[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal LuaValue GetArgumentOrDefault(int index, LuaValue defaultValue = default)
    {
        if (ArgumentCount <= index)
        {
            return defaultValue;
        }

        return Arguments[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetArgument<T>(int index)
    {
        ThrowIfArgumentNotExists(index);

        var arg = Arguments[index];
        if (!arg.TryRead<T>(out var argValue))
        {
            var t = typeof(T);
            if ((t == typeof(int) || t == typeof(long)) && arg.TryReadNumber(out _))
            {
                LuaRuntimeException.BadArgumentNumberIsNotInteger(State.GetTraceback(), index + 1, Thread.GetCurrentFrame().Function.Name);
            }
            else if (LuaValue.TryGetLuaValueType(t, out var type))
            {
                LuaRuntimeException.BadArgument(State.GetTraceback(), index + 1, Thread.GetCurrentFrame().Function.Name, type.ToString(), arg.Type.ToString());
            }
            else if (arg.Type is LuaValueType.UserData or LuaValueType.LightUserData)
            {
                LuaRuntimeException.BadArgument(State.GetTraceback(), index + 1, Thread.GetCurrentFrame().Function.Name, t.Name, arg.UnsafeRead<object>()?.GetType().ToString() ?? "userdata: 0");
            }
            else
            {
                LuaRuntimeException.BadArgument(State.GetTraceback(), index + 1, Thread.GetCurrentFrame().Function.Name, t.Name, arg.Type.ToString());
            }
        }

        return argValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal T GetArgumentOrDefault<T>(int index, T defaultValue = default!)
    {
        if (ArgumentCount <= index)
        {
            return defaultValue;
        }

        var arg = Arguments[index];

        if (arg.Type is LuaValueType.Nil)
        {
            return defaultValue;
        }

        if (!arg.TryRead<T>(out var argValue))
        {
            var t = typeof(T);
            if ((t == typeof(int) || t == typeof(long)) && arg.TryReadNumber(out _))
            {
                LuaRuntimeException.BadArgumentNumberIsNotInteger(State.GetTraceback(), index + 1, Thread.GetCurrentFrame().Function.Name);
            }
            else if (LuaValue.TryGetLuaValueType(t, out var type))
            {
                LuaRuntimeException.BadArgument(State.GetTraceback(), index + 1, Thread.GetCurrentFrame().Function.Name, type.ToString(), arg.Type.ToString());
            }
            else if (arg.Type is LuaValueType.UserData or LuaValueType.LightUserData)
            {
                LuaRuntimeException.BadArgument(State.GetTraceback(), index + 1, Thread.GetCurrentFrame().Function.Name, t.Name, arg.UnsafeRead<object>()?.GetType().ToString() ?? "userdata: 0");
            }
            else
            {
                LuaRuntimeException.BadArgument(State.GetTraceback(), index + 1, Thread.GetCurrentFrame().Function.Name, t.Name, arg.Type.ToString());
            }
        }

        return argValue;
    }

    public CsClosure? GetCsClosure()
    {
        return Thread.GetCurrentFrame().Function as CsClosure;
    }

    internal void ThrowBadArgument(int index, string message)
    {
        LuaRuntimeException.BadArgument(State.GetTraceback(), index, Thread.GetCurrentFrame().Function.Name, message);
    }

    void ThrowIfArgumentNotExists(int index)
    {
        if (ArgumentCount <= index)
        {
            LuaRuntimeException.BadArgument(State.GetTraceback(), index + 1, Thread.GetCurrentFrame().Function.Name);
        }
    }
}