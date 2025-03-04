using System.Runtime.CompilerServices;
using Lua.Runtime;
using Lua.Internal;

namespace Lua.Standard;

public class DebugLibrary
{
    public static readonly DebugLibrary Instance = new();

    public DebugLibrary()
    {
        Functions =
        [
            new("getlocal", GetLocal),
            new("setlocal", SetLocal),
            new("getupvalue", GetUpValue),
            new("setupvalue", SetUpValue),
            new("getmetatable", GetMetatable),
            new("setmetatable", SetMetatable),
            new("getuservalue", GetUserValue),
            new("setuservalue", SetUserValue),
            new("traceback", Traceback),
            new("getregistry", GetRegistry),
            new("upvalueid", UpValueId),
            new("upvaluejoin", UpValueJoin),
            new("gethook", GetHook),
            new("sethook", SetHook),
            new("getinfo", GetInfo),
        ];
    }

    public readonly LuaFunction[] Functions;


    static LuaThread GetLuaThread(in LuaFunctionExecutionContext context, out int argOffset)
    {
        if (context.ArgumentCount < 1)
        {
            argOffset = 0;
            return context.Thread;
        }

        if (context.GetArgument(0).TryRead<LuaThread>(out var thread))
        {
            argOffset = 1;
            return thread;
        }

        argOffset = 0;
        return context.Thread;
    }


    static ref LuaValue FindLocal(LuaThread thread, int level, int index, out string? name)
    {
        if (index == 0)
        {
            name = null;
            return ref Unsafe.NullRef<LuaValue>();
        }

        var callStack = thread.GetCallStackFrames();
        var frame = callStack[^(level + 1)];
        if (index < 0)
        {
            index = -index - 1;
            var frameVariableArgumentCount = frame.VariableArgumentCount;
            if (frameVariableArgumentCount > 0 && index < frameVariableArgumentCount)
            {
                name = "(*vararg)";
                return ref thread.Stack.Get(frame.Base - frameVariableArgumentCount + index);
            }

            name = null;
            return ref Unsafe.NullRef<LuaValue>();
        }

        index -= 1;


        var frameBase = frame.Base;


        if (frame.Function is Closure closure)
        {
            var locals = closure.Proto.Locals;
            var nextFrame = callStack[^level];
            var currentPc = nextFrame.CallerInstructionIndex;
            {
                int nextFrameBase = (closure.Proto.Instructions[currentPc].OpCode is OpCode.Call or OpCode.TailCall) ? nextFrame.Base - 1 : nextFrame.Base;
                if (nextFrameBase - 1 < frameBase + index)
                {
                    name = null;
                    return ref Unsafe.NullRef<LuaValue>();
                }
            }
            foreach (var local in locals)
            {
                if (local.Index == index && currentPc >= local.StartPc && currentPc < local.EndPc)
                {
                    name = local.Name.ToString();
                    return ref thread.Stack.Get(frameBase + local.Index);
                }

                if (local.Index > index)
                {
                    break;
                }
            }
        }
        else
        {
            int nextFrameBase = level != 0 ? callStack[^level].Base : thread.Stack.Count;

            if (nextFrameBase - 1 < frameBase + index)
            {
                name = null;
                return ref Unsafe.NullRef<LuaValue>();
            }
        }

        name = "(*temporary)";
        return ref thread.Stack.Get(frameBase + index);
    }

    public ValueTask<int> GetLocal(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        static LuaValue GetParam(LuaFunction function, int index)
        {
            if (function is Closure closure)
            {
                var paramCount = closure.Proto.ParameterCount;
                if (0 <= index && index < paramCount)
                {
                    return closure.Proto.Locals[index].Name.ToString();
                }
            }

            return LuaValue.Nil;
        }

        var thread = GetLuaThread(context, out var argOffset);

        var index = context.GetArgument<int>(argOffset + 1);
        if (context.GetArgument(argOffset).TryReadFunction(out var f))
        {
            buffer.Span[0] = GetParam(f, index - 1);
            return new(1);
        }

        var level = context.GetArgument<int>(argOffset);


        if (level < 0 || level >= thread.GetCallStackFrames().Length)
        {
            context.ThrowBadArgument(1, "level out of range");
        }

        ref var local = ref FindLocal(thread, level, index, out var name);
        if (name is null)
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }

        buffer.Span[0] = name;
        buffer.Span[1] = local;
        return new(2);
    }

    public ValueTask<int> SetLocal(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var thread = GetLuaThread(context, out var argOffset);

        var value = context.GetArgument(argOffset + 2);
        var index = context.GetArgument<int>(argOffset + 1);
        var level = context.GetArgument<int>(argOffset);


        if (level < 0 || level >= thread.GetCallStackFrames().Length)
        {
            context.ThrowBadArgument(1, "level out of range");
        }

        ref var local = ref FindLocal(thread, level, index, out var name);
        if (name is null)
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }

        buffer.Span[0] = name;
        local = value;
        return new(1);
    }

    public ValueTask<int> GetUpValue(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var func = context.GetArgument<LuaFunction>(0);
        var index = context.GetArgument<int>(1) - 1;
        if (func is not Closure closure)
        {
            if (func is CsClosure csClosure)
            {
                var upValues = csClosure.UpValues;
                if (index < 0 || index >= upValues.Length)
                {
                    return new(0);
                }

                buffer.Span[0] = "";
                buffer.Span[1] = upValues[index];
                return new(1);
            }

            return new(0);
        }

        {
            var upValues = closure.UpValues;
            var descriptions = closure.Proto.UpValues;
            if (index < 0 || index >= descriptions.Length)
            {
                return new(0);
            }

            var description = descriptions[index];
            buffer.Span[0] = description.Name.ToString();
            buffer.Span[1] = upValues[index].GetValue();
            return new(2);
        }
    }

    public ValueTask<int> SetUpValue(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var func = context.GetArgument<LuaFunction>(0);
        var index = context.GetArgument<int>(1) - 1;
        var value = context.GetArgument(2);
        if (func is not Closure closure)
        {
            if (func is CsClosure csClosure)
            {
                var upValues = csClosure.UpValues;
                if (index >= 0 && index < upValues.Length)
                {
                    buffer.Span[0] = "";
                    upValues[index] = value;
                    return new(0);
                }

                return new(0);
            }

            return new(0);
        }

        {
            var upValues = closure.UpValues;
            var descriptions = closure.Proto.UpValues;
            if (index < 0 || index >= descriptions.Length)
            {
                return new(0);
            }

            var description = descriptions[index];
            buffer.Span[0] = description.Name.ToString();
            upValues[index].SetValue(value);
            return new(1);
        }
    }

    public ValueTask<int> GetMetatable(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (context.State.TryGetMetatable(arg0, out var table))
        {
            buffer.Span[0] = table;
        }
        else
        {
            buffer.Span[0] = LuaValue.Nil;
        }

        return new(1);
    }

    public ValueTask<int> SetMetatable(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);
        var arg1 = context.GetArgument(1);

        if (arg1.Type is not (LuaValueType.Nil or LuaValueType.Table))
        {
            LuaRuntimeException.BadArgument(context.State.GetTraceback(), 2, "setmetatable", [LuaValueType.Nil, LuaValueType.Table]);
        }

        context.State.SetMetatable(arg0, arg1.UnsafeRead<LuaTable>());

        buffer.Span[0] = arg0;
        return new(1);
    }

    public ValueTask<int> GetUserValue(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        if (!context.GetArgumentOrDefault(0).TryRead<ILuaUserData>(out var iUserData))
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }

        var index = 1; // context.GetArgument<int>(1); //for lua 5.4
        var userValues = iUserData.UserValues;
        if (index > userValues.Length
            //index < 1 ||  // for lua 5.4
           )
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }

        buffer.Span[0] = userValues[index - 1];
        return new(1);
    }

    public ValueTask<int> SetUserValue(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var iUserData = context.GetArgument<ILuaUserData>(0);
        var value = context.GetArgument(1);
        var index = 1; // context.GetArgument<int>(2);// for lua 5.4
        var userValues = iUserData.UserValues;
        if (index > userValues.Length
            //|| index < 1 // for lua 5.4
           )
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }

        userValues[index - 1] = value;
        buffer.Span[0] = new LuaValue(iUserData);
        return new(1);
    }

    public ValueTask<int> Traceback(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var thread = GetLuaThread(context, out var argOffset);

        var message = context.GetArgumentOrDefault(argOffset);
        var level = context.GetArgumentOrDefault<int>(argOffset + 1, argOffset == 0 ? 1 : 0);

        if (message.Type is not (LuaValueType.Nil or LuaValueType.String or LuaValueType.Number))
        {
            buffer.Span[0] = message;
            return new(1);
        }

        if (level < 0)
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }

        if (thread is LuaCoroutine coroutine)
        {
            if (coroutine.LuaTraceback is not null)
            {
                buffer.Span[0] = coroutine.LuaTraceback.ToString(level);
                return new(1);
            }
        }

        var callStack = thread.GetCallStackFrames();
        if (callStack.Length == 0)
        {
            buffer.Span[0] = "stack traceback:";
            return new(1);
        }

        var skipCount = Math.Min(Math.Max(level - 1, 0), callStack.Length - 1);
        var frames = callStack[1..^skipCount];
        buffer.Span[0] = Runtime.Traceback.GetTracebackString(context.State, (Closure)callStack[0].Function, frames, message, level == 1);
        return new(1);
    }

    public ValueTask<int> GetRegistry(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        buffer.Span[0] = context.State.Registry;
        return new(1);
    }

    public ValueTask<int> UpValueId(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var n1 = context.GetArgument<int>(1);
        var f1 = context.GetArgument<LuaFunction>(0);

        if (f1 is not Closure closure)
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }

        var upValues = closure.GetUpValuesSpan();
        if (n1 <= 0 || n1 > upValues.Length)
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }

        buffer.Span[0] = new LuaValue(upValues[n1 - 1]);
        return new(1);
    }

    public ValueTask<int> UpValueJoin(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var n2 = context.GetArgument<int>(3);
        var f2 = context.GetArgument<LuaFunction>(2);
        var n1 = context.GetArgument<int>(1);
        var f1 = context.GetArgument<LuaFunction>(0);

        if (f1 is not Closure closure1 || f2 is not Closure closure2)
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }

        var upValues1 = closure1.GetUpValuesSpan();
        var upValues2 = closure2.GetUpValuesSpan();
        if (n1 <= 0 || n1 > upValues1.Length)
        {
            context.ThrowBadArgument(1, "invalid upvalue index");
        }

        if (n2 < 0 || n2 > upValues2.Length)
        {
            context.ThrowBadArgument(3, "invalid upvalue index");
        }

        upValues1[n1 - 1] = upValues2[n2 - 1];
        return new(0);
    }

    public async ValueTask<int> SetHook(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var thread = GetLuaThread(context, out var argOffset);
        LuaFunction? hook = context.GetArgumentOrDefault<LuaFunction?>(argOffset);
        if (hook is null)
        {
            thread.HookCount = -1;
            thread.BaseHookCount = 0;
            thread.IsCountHookEnabled = false;
            thread.Hook = null;
            thread.IsLineHookEnabled = false;
            thread.IsCallHookEnabled = false;
            thread.IsReturnHookEnabled = false;
            return 0;
        }

        var mask = context.GetArgument<string>(argOffset + 1);
        if (context.HasArgument(argOffset + 2))
        {
            var count = context.GetArgument<int>(argOffset + 2);
            thread.BaseHookCount = count;
            thread.HookCount = count;
            if (count > 0)
            {
                thread.IsCountHookEnabled = true;
            }
        }
        else
        {
            thread.HookCount = 0;
            thread.BaseHookCount = 0;
            thread.IsCountHookEnabled = false;
        }

        thread.IsLineHookEnabled = (mask.Contains('l'));
        thread.IsCallHookEnabled = (mask.Contains('c'));
        thread.IsReturnHookEnabled = (mask.Contains('r'));

        if (thread.IsLineHookEnabled)
        {
            thread.LastPc = thread.CallStack.Count > 0 ? thread.GetCurrentFrame().CallerInstructionIndex : -1;
        }

        thread.Hook = hook;
        if (thread.IsReturnHookEnabled && context.Thread == thread)
        {
            var stack = thread.Stack;
            stack.Push("return");
            stack.Push(LuaValue.Nil);
            var funcContext = new LuaFunctionExecutionContext
            {
                State = context.State,
                Thread = context.Thread,
                ArgumentCount = 2,
                FrameBase = stack.Count - 2,
            };
            var frame = new CallStackFrame
            {
                Base = funcContext.FrameBase,
                VariableArgumentCount = hook.GetVariableArgumentCount(2),
                Function = hook,
            };
            frame.Flags |= CallStackFrameFlags.InHook;
            thread.PushCallStackFrame(frame);
            try
            {
                thread.IsInHook = true;
                await hook.Func(funcContext, Memory<LuaValue>.Empty, cancellationToken);
            }
            finally
            {
                thread.IsInHook = false;
            }

            thread.PopCallStackFrame();
        }

        return 0;
    }


    public ValueTask<int> GetHook(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var thread = GetLuaThread(context, out var argOffset);
        if (thread.Hook is null)
        {
            buffer.Span[0] = LuaValue.Nil;
            buffer.Span[1] = LuaValue.Nil;
            buffer.Span[2] = LuaValue.Nil;
            return new(3);
        }

        buffer.Span[0] = thread.Hook;
        buffer.Span[1] = (
            (thread.IsCallHookEnabled ? "c" : "") +
            (thread.IsReturnHookEnabled ? "r" : "") +
            (thread.IsLineHookEnabled ? "l" : "")
        );
        buffer.Span[2] = thread.BaseHookCount;
        return new(3);
    }

    public ValueTask<int> GetInfo(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        //return new(0);
        var thread = GetLuaThread(context, out var argOffset);
        string what = context.GetArgumentOrDefault<string>(argOffset + 1, "flnStu");
        CallStackFrame? previousFrame = null;
        CallStackFrame? currentFrame = null;
        int pc = 0;
        var arg1 = context.GetArgument(argOffset);

        if (arg1.TryReadFunction(out var functionToInspect))
        {
            //what = ">" + what;
        }
        else if (arg1.TryReadNumber(out _))
        {
            var level = context.GetArgument<int>(argOffset) + 1;

            var callStack = thread.GetCallStackFrames();

            if (level <= 0 || level > callStack.Length)
            {
                buffer.Span[0] = LuaValue.Nil;
                return new(1);
            }


            currentFrame = thread.GetCallStackFrames()[^(level)];
            previousFrame = level + 1 <= callStack.Length ? callStack[^(level + 1)] : null;
            if (level != 1)
            {
                pc = thread.GetCallStackFrames()[^(level - 1)].CallerInstructionIndex;
            }

            functionToInspect = currentFrame.Value.Function;
        }
        else
        {
            context.ThrowBadArgument(argOffset, "function or level expected");
        }

        using var debug = LuaDebug.Create(context.State, previousFrame, currentFrame, functionToInspect, pc, what, out var isValid);
        if (!isValid)
        {
            context.ThrowBadArgument(argOffset + 1, "invalid option");
        }

        var table = new LuaTable(0, 1);
        if (what.Contains('S'))
        {
            table["source"] = debug.Source ?? LuaValue.Nil;
            table["short_src"] = debug.ShortSource.ToString();
            table["linedefined"] = debug.LineDefined;
            table["lastlinedefined"] = debug.LastLineDefined;
            table["what"] = debug.What ?? LuaValue.Nil;
            ;
        }

        if (what.Contains('l'))
        {
            table["currentline"] = debug.CurrentLine;
        }

        if (what.Contains('u'))
        {
            table["nups"] = debug.UpValueCount;
            table["nparams"] = debug.ParameterCount;
            table["isvararg"] = debug.IsVarArg;
        }

        if (what.Contains('n'))
        {
            table["name"] = debug.Name ?? LuaValue.Nil;
            ;
            table["namewhat"] = debug.NameWhat ?? LuaValue.Nil;
            ;
        }

        if (what.Contains('t'))
        {
            table["istailcall"] = debug.IsTailCall;
        }

        if (what.Contains('f'))
        {
            table["func"] = functionToInspect;
        }

        if (what.Contains('L'))
        {
            if (functionToInspect is Closure closure)
            {
                var activeLines = new LuaTable(0, 8);
                foreach (var pos in closure.Proto.SourcePositions)
                {
                    activeLines[pos.Line] = true;
                }

                table["activelines"] = activeLines;
            }
        }

        buffer.Span[0] = table;

        return new(1);
    }
}