using Lua.Runtime;

namespace Lua.Standard;

public sealed class CoroutineLibrary
{
    public static readonly CoroutineLibrary Instance = new();

    public CoroutineLibrary()
    {
        Functions = [
            new("create", Create),
            new("resume", Resume),
            new("running", Running),
            new("status", Status),
            new("wrap", Wrap),
            new("yield", Yield),
        ];
    }

    public readonly LuaFunction[] Functions;

    public ValueTask<int> Create(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaFunction>(0);
        buffer.Span[0] = new LuaCoroutine(arg0, true);
        return new(1);
    }

    public ValueTask<int> Resume(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var thread = context.GetArgument<LuaThread>(0);
        return thread.ResumeAsync(context, buffer, cancellationToken);
    }

    public ValueTask<int> Running(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        buffer.Span[0] = context.Thread;
        buffer.Span[1] = context.Thread == context.State.MainThread;
        return new(2);
    }

    public ValueTask<int> Status(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var thread = context.GetArgument<LuaThread>(0);
        buffer.Span[0] = thread.GetStatus() switch
        {
            LuaThreadStatus.Normal => "normal",
            LuaThreadStatus.Suspended => "suspended",
            LuaThreadStatus.Running => "running",
            LuaThreadStatus.Dead => "dead",
            _ => "",
        };
        return new(1);
    }

    public ValueTask<int> Wrap(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaFunction>(0);
        var thread = new LuaCoroutine(arg0, false);

        buffer.Span[0] = new CsClosure("wrap", [thread],static async (context, buffer, cancellationToken) =>
        {
            var thread = context.GetCsClosure()!.UpValues[0].Read<LuaThread>();
            if (thread is not LuaCoroutine coroutine)
            {
                return await thread.ResumeAsync(context, buffer, cancellationToken);
            }
            var stack = context.Thread.Stack;
            var frameBase = stack.Count;

            stack.Push(thread);
            stack.PushRange(context.Arguments);
            context.Thread.PushCallStackFrame(new()
            {
                Base = frameBase,
                VariableArgumentCount = 0,
                Function = coroutine.Function,
            });
            try
            {
                var resultCount = await thread.ResumeAsync(context with
                {
                    ArgumentCount = context.ArgumentCount + 1,
                    FrameBase = frameBase,
                }, buffer, cancellationToken);

                buffer.Span[1..].CopyTo(buffer.Span[0..]);
                return resultCount - 1;
            }
            finally
            {
                context.Thread.PopCallStackFrame();
            }

           
        });

        return new(1);
    }

    public ValueTask<int> Yield(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        return context.Thread.YieldAsync(context, buffer, cancellationToken);
    }
}