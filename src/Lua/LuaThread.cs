using System.Runtime.CompilerServices;
using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public abstract class LuaThread
{
    public abstract LuaThreadStatus GetStatus();
    public abstract void UnsafeSetStatus(LuaThreadStatus status);
    public abstract ValueTask<int> ResumeAsync(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken = default);
    public abstract ValueTask<int> YieldAsync(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken = default);

    LuaStack stack = new();
    FastStackCore<CallStackFrame> callStack;

    internal LuaStack Stack => stack;
    internal ref FastStackCore<CallStackFrame> CallStack => ref callStack;

    internal bool IsLineHookEnabled
    {
        get => LineAndCountHookMask.Flag0;
        set => LineAndCountHookMask.Flag0 = value;
    }

    internal bool IsCountHookEnabled
    {
        get => LineAndCountHookMask.Flag1;
        set => LineAndCountHookMask.Flag1 = value;
    }

    internal BitFlags2 LineAndCountHookMask;

    internal bool IsCallHookEnabled
    {
        get => CallOrReturnHookMask.Flag0;
        set => CallOrReturnHookMask.Flag0 = value;
    }

    internal bool IsReturnHookEnabled
    {
        get => CallOrReturnHookMask.Flag1;
        set => CallOrReturnHookMask.Flag1 = value;
    }

    internal BitFlags2 CallOrReturnHookMask;
    internal bool IsInHook;
    internal int HookCount;
    internal int BaseHookCount;
    internal int LastPc;
    internal LuaFunction? Hook { get; set; }

    public ref readonly CallStackFrame GetCurrentFrame()
    {
        return ref callStack.PeekRef();
    }

    public ReadOnlySpan<LuaValue> GetStackValues()
    {
        return stack.AsSpan();
    }

    public ReadOnlySpan<CallStackFrame> GetCallStackFrames()
    {
        return callStack.AsSpan();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PushCallStackFrame(in CallStackFrame frame)
    {
        callStack.Push(frame);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrame()
    {
        if (callStack.TryPop(out var frame))
        {
            stack.PopUntil(frame.Base);
        }
        else
        {
            ThrowForEmptyStack();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrameUnsafe(int frameBase)
    {
        if (callStack.TryPop())
        {
            stack.PopUntil(frameBase);
        }
        else
        {
            ThrowForEmptyStack();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void PopCallStackFrameUnsafe()
    {
        if (!callStack.TryPop())
        {
            ThrowForEmptyStack();
        }
    }

    internal void DumpStackValues()
    {
        var span = GetStackValues();
        for (int i = 0; i < span.Length; i++)
        {
            Console.WriteLine($"LuaStack [{i}]\t{span[i]}");
        }
    }

    static void ThrowForEmptyStack() => throw new InvalidOperationException("Empty stack");
}