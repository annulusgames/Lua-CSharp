using System.Buffers;
using System.Threading.Tasks.Sources;
using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public sealed class LuaCoroutine : LuaThread, IValueTaskSource<LuaCoroutine.YieldContext>, IValueTaskSource<LuaCoroutine.ResumeContext>
{
    struct YieldContext
    {
        public required LuaValue[] Results;
    }

    struct ResumeContext
    {
        public required LuaValue[] Results;
    }

    byte status;
    bool isFirstCall = true;
    ValueTask<int> functionTask;
    LuaValue[] buffer;

    ManualResetValueTaskSourceCore<ResumeContext> resume;
    ManualResetValueTaskSourceCore<YieldContext> yield;
    Traceback? traceback;

    public LuaCoroutine(LuaFunction function, bool isProtectedMode)
    {
        IsProtectedMode = isProtectedMode;
        Function = function;

        buffer = ArrayPool<LuaValue>.Shared.Rent(1024);
        buffer.AsSpan().Clear();
    }

    public override LuaThreadStatus GetStatus() => (LuaThreadStatus)status;

    public override void UnsafeSetStatus(LuaThreadStatus status)
    {
        this.status = (byte)status;
    }

    public bool IsProtectedMode { get; }
    public LuaFunction Function { get; }
    
    
    internal Traceback? LuaTraceback => traceback;

    public override async ValueTask<int> ResumeAsync(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        var baseThread = context.Thread;
        baseThread.UnsafeSetStatus(LuaThreadStatus.Normal);

        context.State.ThreadStack.Push(this);
        try
        {
            switch ((LuaThreadStatus)Volatile.Read(ref status))
            {
                case LuaThreadStatus.Suspended:
                    Volatile.Write(ref status, (byte)LuaThreadStatus.Running);

                    if (isFirstCall)
                    {
                        // copy stack value
                        var argCount = context.ArgumentCount;
                        Stack.EnsureCapacity(argCount);
                        baseThread.Stack.AsSpan()[^argCount..].CopyTo(Stack.GetBuffer());
                        Stack.NotifyTop(argCount);
                    }
                    else
                    {
                        yield.SetResult(new()
                        {
                            Results = context.ArgumentCount == 1
                                ? []
                                : context.Arguments[1..].ToArray()
                        });
                    }

                    break;
                case LuaThreadStatus.Normal:
                case LuaThreadStatus.Running:
                    if (IsProtectedMode)
                    {
                        buffer.Span[0] = false;
                        buffer.Span[1] = "cannot resume non-suspended coroutine";
                        return 2;
                    }
                    else
                    {
                        throw new LuaRuntimeException(context.State.GetTraceback(), "cannot resume non-suspended coroutine");
                    }
                case LuaThreadStatus.Dead:
                    if (IsProtectedMode)
                    {
                        buffer.Span[0] = false;
                        buffer.Span[1] = "cannot resume dead coroutine";
                        return 2;
                    }
                    else
                    {
                        throw new LuaRuntimeException(context.State.GetTraceback(), "cannot resume dead coroutine");
                    }
            }

            var resumeTask = new ValueTask<ResumeContext>(this, resume.Version);

            CancellationTokenRegistration registration = default;
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.UnsafeRegister(static x =>
                {
                    var coroutine = (LuaCoroutine)x!;
                    coroutine.yield.SetException(new OperationCanceledException());
                }, this);
            }

            try
            {
                if (isFirstCall)
                {
                    int frameBase;
                    var variableArgumentCount = Function.GetVariableArgumentCount(context.ArgumentCount - 1);

                    if (variableArgumentCount > 0)
                    {
                        var fixedArgumentCount = context.ArgumentCount - 1 - variableArgumentCount;
                        var args = context.Arguments;

                        Stack.PushRange(args.Slice(1 + fixedArgumentCount, variableArgumentCount));

                        frameBase = Stack.Count;

                        Stack.PushRange(args.Slice(1, fixedArgumentCount));
                    }
                    else
                    {
                        frameBase = Stack.Count;

                        Stack.PushRange(context.Arguments[1..]);
                    }

                    functionTask = Function.InvokeAsync(new()
                    {
                        State = context.State,
                        Thread = this,
                        ArgumentCount = context.ArgumentCount - 1,
                        FrameBase = frameBase
                    }, this.buffer, cancellationToken).Preserve();

                    Volatile.Write(ref isFirstCall, false);
                }

                var (index, result0, result1) = await ValueTaskEx.WhenAny(resumeTask, functionTask!);

                var bufferSpan = buffer.Span;
                if (index == 0)
                {
                    var results = result0.Results;

                    bufferSpan[0] = true;
                    results.CopyTo(bufferSpan[1..]);

                    return results.Length + 1;
                }
                else
                {
                    var resultCount = functionTask!.Result;

                    Volatile.Write(ref status, (byte)LuaThreadStatus.Dead);
                    bufferSpan[0] = true;
                    this.buffer.AsSpan()[..resultCount].CopyTo(bufferSpan[1..]);

                    ArrayPool<LuaValue>.Shared.Return(this.buffer);

                    return 1 + resultCount;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (IsProtectedMode)
                {
                    ArrayPool<LuaValue>.Shared.Return(this.buffer);
                    traceback = (ex as LuaRuntimeException)?.LuaTraceback;
                    Volatile.Write(ref status, (byte)LuaThreadStatus.Dead);
                    buffer.Span[0] = false;
                    buffer.Span[1] = ex is LuaRuntimeException { ErrorObject: not null } luaEx ? luaEx.ErrorObject.Value : ex.Message;
                    return 2;
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                registration.Dispose();
                resume.Reset();
            }
        }
        finally
        {
            context.State.ThreadStack.Pop();
            baseThread.UnsafeSetStatus(LuaThreadStatus.Running);
        }
    }

    public override async ValueTask<int> YieldAsync(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref status) != (byte)LuaThreadStatus.Running)
        {
            throw new LuaRuntimeException(context.State.GetTraceback(), "cannot call yield on a coroutine that is not currently running");
        }

        if (context.Thread.GetCallStackFrames()[^2].Function is not Closure)
        {
            throw new LuaRuntimeException(context.State.GetTraceback(), "attempt to yield across a C#-call boundary");
        }

        resume.SetResult(new()
        {
            Results = context.Arguments.ToArray(),
        });

        Volatile.Write(ref status, (byte)LuaThreadStatus.Suspended);

        CancellationTokenRegistration registration = default;
        if (cancellationToken.CanBeCanceled)
        {
            registration = cancellationToken.UnsafeRegister(static x =>
            {
                var coroutine = (LuaCoroutine)x!;
                coroutine.yield.SetException(new OperationCanceledException());
            }, this);
        }

    RETRY:
        try
        {
            var result = await new ValueTask<YieldContext>(this, yield.Version);
            for (int i = 0; i < result.Results.Length; i++)
            {
                buffer.Span[i] = result.Results[i];
            }

            return result.Results.Length;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            yield.Reset();
            goto RETRY;
        }
        finally
        {
            registration.Dispose();
            yield.Reset();
        }
    }

    YieldContext IValueTaskSource<YieldContext>.GetResult(short token)
    {
        return yield.GetResult(token);
    }

    ValueTaskSourceStatus IValueTaskSource<YieldContext>.GetStatus(short token)
    {
        return yield.GetStatus(token);
    }

    void IValueTaskSource<YieldContext>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        yield.OnCompleted(continuation, state, token, flags);
    }

    ResumeContext IValueTaskSource<ResumeContext>.GetResult(short token)
    {
        return resume.GetResult(token);
    }

    ValueTaskSourceStatus IValueTaskSource<ResumeContext>.GetStatus(short token)
    {
        return resume.GetStatus(token);
    }

    void IValueTaskSource<ResumeContext>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        resume.OnCompleted(continuation, state, token, flags);
    }
}