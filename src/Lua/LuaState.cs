using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Lua.Internal;
using Lua.Loaders;
using Lua.Runtime;

namespace Lua;

public sealed class LuaState
{
    public const string DefaultChunkName = "chunk";

    // states
    readonly LuaMainThread mainThread = new();
    FastListCore<UpValue> openUpValues;
    FastStackCore<LuaThread> threadStack;
    readonly LuaTable packages = new();
    readonly LuaTable environment;
    readonly LuaTable registry = new();
    readonly UpValue envUpValue;
    bool isRunning;

    FastStackCore<LuaDebug.LuaDebugBuffer> debugBufferPool;

    internal UpValue EnvUpValue => envUpValue;
    internal ref FastStackCore<LuaThread> ThreadStack => ref threadStack;
    internal ref FastListCore<UpValue> OpenUpValues => ref openUpValues;
    internal ref FastStackCore<LuaDebug.LuaDebugBuffer> DebugBufferPool => ref debugBufferPool;

    public LuaTable Environment => environment;
    public LuaTable Registry => registry;
    public LuaTable LoadedModules => packages;
    public LuaMainThread MainThread => mainThread;
    public LuaThread CurrentThread
    {
        get
        {
            if (threadStack.TryPeek(out var thread)) return thread;
            return mainThread;
        }
    }

    public ILuaModuleLoader ModuleLoader { get; set; } = FileModuleLoader.Instance;

    // metatables
    LuaTable? nilMetatable;
    LuaTable? numberMetatable;
    LuaTable? stringMetatable;
    LuaTable? booleanMetatable;
    LuaTable? functionMetatable;
    LuaTable? threadMetatable;

    public static LuaState Create()
    {
        return new();
    }

    LuaState()
    {
        environment = new();
        envUpValue = UpValue.Closed(environment);
    }

    public async ValueTask<int> RunAsync(Chunk chunk, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfRunning();

        Volatile.Write(ref isRunning, true);
        try
        {
            var closure = new Closure(this, chunk);
            return await closure.InvokeAsync(new()
            {
                State = this,
                Thread = CurrentThread,
                ArgumentCount = 0,
                FrameBase = 0,
                SourcePosition = null,
                RootChunkName = chunk.Name,
                ChunkName = chunk.Name,
            }, buffer, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref isRunning, false);
        }
    }

    public void Push(LuaValue value)
    {
        CurrentThread.Stack.Push(value);
    }

    public Traceback GetTraceback()
    {
        if (threadStack.Count == 0)
        {
            return new(this)
            {
                RootFunc = (Closure)MainThread.GetCallStackFrames()[0].Function,
                StackFrames = MainThread.GetCallStackFrames()[1..]
                    .ToArray()
            };
        }

        using var list = new PooledList<CallStackFrame>(8);
        foreach (var frame in MainThread.GetCallStackFrames()[1..])
        {
            list.Add(frame);
        }

        foreach (var thread in threadStack.AsSpan())
        {
            if (thread.CallStack.Count == 0) continue;
            foreach (var frame in thread.GetCallStackFrames()[1..])
            {
                list.Add(frame);
            }
        }

        return new(this)
        {
            RootFunc = (Closure)MainThread.GetCallStackFrames()[0].Function,
            StackFrames = list.AsSpan().ToArray()
        };
    }

    internal Traceback GetTraceback(LuaThread thread)
    {
        using var list = new PooledList<CallStackFrame>(8);
        foreach (var frame in thread.GetCallStackFrames()[1..])
        {
            list.Add(frame);
        }
        Closure rootFunc;
        if (thread.GetCallStackFrames()[0].Function is Closure closure)
        {
            rootFunc = closure;
        }
        else
        {
            rootFunc = (Closure)MainThread.GetCallStackFrames()[0].Function;
        }

        return new(this)
        {
            RootFunc = rootFunc,
            StackFrames = list.AsSpan().ToArray()
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetMetatable(LuaValue value, [NotNullWhen(true)] out LuaTable? result)
    {
        result = value.Type switch
        {
            LuaValueType.Nil => nilMetatable,
            LuaValueType.Boolean => booleanMetatable,
            LuaValueType.String => stringMetatable,
            LuaValueType.Number => numberMetatable,
            LuaValueType.Function => functionMetatable,
            LuaValueType.Thread => threadMetatable,
            LuaValueType.UserData => value.UnsafeRead<ILuaUserData>().Metatable,
            LuaValueType.Table => value.UnsafeRead<LuaTable>().Metatable,
            _ => null
        };

        return result != null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetMetatable(LuaValue value, LuaTable metatable)
    {
        switch (value.Type)
        {
            case LuaValueType.Nil:
                nilMetatable = metatable;
                break;
            case LuaValueType.Boolean:
                booleanMetatable = metatable;
                break;
            case LuaValueType.String:
                stringMetatable = metatable;
                break;
            case LuaValueType.Number:
                numberMetatable = metatable;
                break;
            case LuaValueType.Function:
                functionMetatable = metatable;
                break;
            case LuaValueType.Thread:
                threadMetatable = metatable;
                break;
            case LuaValueType.UserData:
                value.UnsafeRead<ILuaUserData>().Metatable = metatable;
                break;
            case LuaValueType.Table:
                value.UnsafeRead<LuaTable>().Metatable = metatable;
                break;
        }
    }

    internal UpValue GetOrAddUpValue(LuaThread thread, int registerIndex)
    {
        foreach (var upValue in openUpValues.AsSpan())
        {
            if (upValue.RegisterIndex == registerIndex && upValue.Thread == thread)
            {
                return upValue;
            }
        }

        var newUpValue = UpValue.Open(thread, registerIndex);
        openUpValues.Add(newUpValue);
        return newUpValue;
    }

    internal void CloseUpValues(LuaThread thread, int frameBase)
    {
        for (int i = 0; i < openUpValues.Length; i++)
        {
            var upValue = openUpValues[i];
            if (upValue.Thread != thread) continue;

            if (upValue.RegisterIndex >= frameBase)
            {
                upValue.Close();
                openUpValues.RemoveAtSwapback(i);
                i--;
            }
        }
    }

    void ThrowIfRunning()
    {
        if (Volatile.Read(ref isRunning))
        {
            throw new InvalidOperationException("the lua state is currently running");
        }
    }
}