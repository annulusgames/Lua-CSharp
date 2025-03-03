using System.Globalization;
using System.Runtime.CompilerServices;
using Lua.CodeAnalysis;
using Lua.Internal;

namespace Lua.Runtime;

public class Traceback(LuaState state)
{
    public LuaState State => state;
    public required Closure RootFunc { get; init; }
    public required CallStackFrame[] StackFrames { get; init; }

    internal string RootChunkName => RootFunc.Proto.Name;

    internal SourcePosition LastPosition
    {
        get
        {
            var stackFrames = StackFrames.AsSpan();
            for (var index = stackFrames.Length - 1; index >= 0; index--)
            {
                LuaFunction lastFunc = index > 0 ? stackFrames[index - 1].Function : RootFunc;
                var frame = stackFrames[index];
                if (!frame.IsTailCall && lastFunc is Closure closure)
                {
                    var p = closure.Proto;
                    if (frame.CallerInstructionIndex < 0 || p.SourcePositions.Length <= frame.CallerInstructionIndex)
                    {
                        Console.WriteLine($"Trace back error");
                        return default;
                    }

                    return p.SourcePositions[frame.CallerInstructionIndex];
                }
            }


            return default;
        }
    }

    public override string ToString()
    {
        return GetTracebackString(State, RootFunc, StackFrames, LuaValue.Nil);
    }
    
    public string ToString(int skipFrames)
    {
        if(skipFrames < 0 || skipFrames >= StackFrames.Length)
        {
            return "stack traceback:\n";
        }
        return GetTracebackString(State, RootFunc, StackFrames[..^skipFrames], LuaValue.Nil);
    }

    internal static string GetTracebackString(LuaState state, Closure rootFunc, ReadOnlySpan<CallStackFrame> stackFrames, LuaValue message, bool skipFirstCsharpCall = false)
    {
        using var list = new PooledList<char>(64);
        if (message.Type is not LuaValueType.Nil)
        {
            list.AddRange(message.ToString());
            list.AddRange("\n");
        }

        list.AddRange("stack traceback:\n");
        var intFormatBuffer = (stackalloc char[15]);
        var shortSourceBuffer = (stackalloc char[59]);
        {
            if (0 < stackFrames.Length && !skipFirstCsharpCall && stackFrames[^1].Function is { } f and not Closure)
            {
                list.AddRange("\t[C#]: in function '");
                list.AddRange(f.Name);
                list.AddRange("'\n");
            }
        }

        for (var index = stackFrames.Length - 1; index >= 0; index--)
        {
            LuaFunction lastFunc = index > 0 ? stackFrames[index - 1].Function : rootFunc;
            if (lastFunc is not null and not Closure)
            {
                list.AddRange("\t[C#]: in function '");
                list.AddRange(lastFunc.Name);
                list.AddRange("'\n");
            }
            else if (lastFunc is Closure closure)
            {
                var frame = stackFrames[index];

                if (frame.IsTailCall)
                {
                    list.AddRange("\t(...tail calls...)\n");
                }

                var p = closure.Proto;
                var root = p.GetRoot();
                list.AddRange("\t");
                var len = LuaDebug.WriteShortSource(root.Name, shortSourceBuffer);
                list.AddRange(shortSourceBuffer[..len]);
                list.AddRange(":");
                if (p.SourcePositions.Length <= frame.CallerInstructionIndex)
                {
                    list.AddRange("Trace back error");
                }
                else
                {
                    p.SourcePositions[frame.CallerInstructionIndex].Line.TryFormat(intFormatBuffer, out var charsWritten, provider: CultureInfo.InvariantCulture);
                    list.AddRange(intFormatBuffer[..charsWritten]);
                }


                list.AddRange(": in ");
                if (root == p)
                {
                    list.AddRange("main chunk");
                    list.AddRange("\n");
                    goto Next;
                }

                if (0 < index && stackFrames[index - 1].Flags.HasFlag(CallStackFrameFlags.InHook))
                {
                    list.AddRange("hook");
                    list.AddRange(" '");
                    list.AddRange("?");
                    list.AddRange("'\n");
                    goto Next;
                }

                foreach (var pair in state.Environment.Dictionary)
                {
                    if (pair.Key.TryReadString(out var name)
                        && pair.Value.TryReadFunction(out var result) &&
                        result == closure)
                    {
                        list.AddRange("function '");
                        list.AddRange(name);
                        list.AddRange("'\n");
                        goto Next;
                    }
                }

                var caller = index > 1 ? stackFrames[index - 2].Function : rootFunc;
                if (index > 0 && caller is Closure callerClosure)
                {
                    var t = LuaDebug.GetFuncName(callerClosure.Proto, stackFrames[index - 1].CallerInstructionIndex, out var name);
                    if (t is not null)
                    {
                        if (t is "global")
                        {
                            list.AddRange("function '");
                            list.AddRange(name);
                            list.AddRange("'\n");
                        }
                        else
                        {
                            list.AddRange(t);
                            list.AddRange(" '");
                            list.AddRange(name);
                            list.AddRange("'\n");
                        }

                        goto Next;
                    }
                }


                list.AddRange("function <");
                list.AddRange(shortSourceBuffer[..len]);
                list.AddRange(":");
                {
                    p.LineDefined.TryFormat(intFormatBuffer, out var charsWritten, provider: CultureInfo.InvariantCulture);
                    list.AddRange(intFormatBuffer[..charsWritten]);
                    list.AddRange(">\n");
                }

            Next: ;
            }
        }

        return list.AsSpan()[..^1].ToString();
    }
}