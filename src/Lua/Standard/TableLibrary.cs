using System.Runtime.CompilerServices;
using System.Text;
using Lua.Internal;
using Lua.Runtime;

namespace Lua.Standard;

public sealed class TableLibrary
{
    public static readonly TableLibrary Instance = new();
    

    public TableLibrary()
    {
        Functions = [
            new("concat", Concat),
            new("insert", Insert),
            new("pack", Pack),
            new("remove", Remove),
            new("sort", Sort),
            new("unpack", Unpack),
        ];
    }

    public readonly LuaFunction[] Functions;

    // TODO: optimize
    static readonly Chunk defaultComparer = new()
    {
        Name = "comp",
        Functions = [],
        Constants = [],
        Instructions = [
            Instruction.Le(1, 0, 1),
            Instruction.LoadBool(2, 1, 1),
            Instruction.LoadBool(2, 0, 0),
            Instruction.Return(2, 2),
        ],
        SourcePositions = [
            default, default, default, default,
        ],
        ParameterCount = 2,
        UpValues = [],
        Locals = [new LocalValueInfo(){Name = "a".AsMemory(),StartPc = 0,Index = 0,EndPc = 4}, new LocalValueInfo(){Name = "b".AsMemory(),StartPc = 0,Index = 1,EndPc = 4}],
        MaxStackPosition = 2,
        HasVariableArguments = false,
        LineDefined = 0,
        LastLineDefined = 0,
    };

    public ValueTask<int> Concat(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.HasArgument(1)
            ? context.GetArgument<string>(1)
            : "";
        var arg2 = context.HasArgument(2)
            ? (long)context.GetArgument<double>(2)
            : 1;
        var arg3 = context.HasArgument(3)
            ? (long)context.GetArgument<double>(3)
            : arg0.ArrayLength;

        var builder = new ValueStringBuilder(512);

        for (long i = arg2; i <= arg3; i++)
        {
            var value = arg0[i];

            if (value.Type is LuaValueType.String)
            {
                builder.Append(value.Read<string>());
            }
            else if (value.Type is LuaValueType.Number)
            {
                builder.Append(value.Read<double>().ToString());
            }
            else
            {
                throw new LuaRuntimeException(context.State.GetTraceback(), $"invalid value ({value.Type}) at index {i} in table for 'concat'");
            }

            if (i != arg3) builder.Append(arg1);
        }

        buffer.Span[0] = builder.ToString();
        return new(1);
    }

    public ValueTask<int> Insert(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var table = context.GetArgument<LuaTable>(0);

        var value = context.HasArgument(2)
            ? context.GetArgument(2)
            : context.GetArgument(1);

        var pos_arg = context.HasArgument(2)
            ? context.GetArgument<double>(1)
            : table.ArrayLength + 1;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, "insert", 2, pos_arg);

        var pos = (int)pos_arg;

        if (pos <= 0 || pos > table.ArrayLength + 1)
        {
            throw new LuaRuntimeException(context.State.GetTraceback(), "bad argument #2 to 'insert' (position out of bounds)");
        }

        table.Insert(pos, value);
        return new(0);
    }

    public ValueTask<int> Pack(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var table = new LuaTable(context.ArgumentCount, 1);

        var span = context.Arguments;
        for (int i = 0; i < span.Length; i++)
        {
            table[i + 1] = span[i];
        }
        table["n"] = span.Length;

        buffer.Span[0] = table;
        return new(1);
    }

    public ValueTask<int> Remove(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var table = context.GetArgument<LuaTable>(0);
        var n_arg = context.HasArgument(1)
            ? context.GetArgument<double>(1)
            : table.ArrayLength;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, "remove", 2, n_arg);

        var n = (int)n_arg;

        if (n <= 0 || n > table.GetArraySpan().Length)
        {
            if (!context.HasArgument(1) && n == 0)
            {
                buffer.Span[0] = LuaValue.Nil;
                return new(1);
            }

            throw new LuaRuntimeException(context.State.GetTraceback(), "bad argument #2 to 'remove' (position out of bounds)");
        }
        else if (n > table.ArrayLength)
        {
            buffer.Span[0] = LuaValue.Nil;
            return new(1);
        }

        buffer.Span[0] = table.RemoveAt(n);
        return new(1);
    }

    public async ValueTask<int> Sort(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.HasArgument(1)
            ? context.GetArgument<LuaFunction>(1)
            : new Closure(context.State, defaultComparer);
        
        context.Thread.PushCallStackFrame(new ()
        {
            Base = context.FrameBase,
            VariableArgumentCount = 0,
            Function = arg1
        });
        try
        {
            await QuickSortAsync(context, arg0.GetArrayMemory(), 0, arg0.ArrayLength - 1, arg1, cancellationToken);
            return 0;
        }
        finally
        {
            context.Thread.PopCallStackFrameUnsafe(context.FrameBase);
        }
    }

    async ValueTask QuickSortAsync(LuaFunctionExecutionContext context, Memory<LuaValue> memory, int low, int high, LuaFunction comparer, CancellationToken cancellationToken)
    {
        if (low < high)
        {
            int pivotIndex = await PartitionAsync(context, memory, low, high, comparer, cancellationToken);
            await QuickSortAsync(context, memory, low, pivotIndex - 1, comparer, cancellationToken);
            await QuickSortAsync(context, memory, pivotIndex + 1, high, comparer, cancellationToken);
        }
    }

    async ValueTask<int> PartitionAsync(LuaFunctionExecutionContext context, Memory<LuaValue> memory, int low, int high, LuaFunction comparer, CancellationToken cancellationToken)
    {
        using var methodBuffer = new PooledArray<LuaValue>(1);

        var pivot = memory.Span[high];
        int i = low - 1;

        for (int j = low; j < high; j++)
        {
            context.State.Push(memory.Span[j]);
            context.State.Push(pivot);
            await comparer.InvokeAsync(context with
            {
                ArgumentCount = 2,
                FrameBase = context.Thread.Stack.Count - context.ArgumentCount,
            }, methodBuffer.AsMemory(), cancellationToken);

            if (methodBuffer[0].ToBoolean())
            {
                i++;
                Swap(memory.Span, i, j);
            }
        }

        Swap(memory.Span, i + 1, high);
        return i + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Swap(Span<LuaValue> span, int i, int j)
    {
        (span[i], span[j]) = (span[j], span[i]);
    }

    public ValueTask<int> Unpack(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.HasArgument(1)
            ? (int)context.GetArgument<double>(1)
            : 1;
        var arg2 = context.HasArgument(2)
            ? (int)context.GetArgument<double>(2)
            : arg0.ArrayLength;

        var index = 0;
        for (int i = arg1; i <= arg2; i++)
        {
            buffer.Span[index] = arg0[i];
            index++;
        }

        return new(index);
    }
}