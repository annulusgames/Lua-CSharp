using System.Runtime.CompilerServices;
using Lua.Internal;

namespace Lua.Runtime;

public sealed class Closure : LuaFunction
{
    Chunk proto;
    FastListCore<UpValue> upValues;

    public Closure(LuaState state, Chunk proto, LuaTable? environment = null)
        : base(proto.Name, (context, buffer, ct) => LuaVirtualMachine.ExecuteClosureAsync(context.State, buffer, ct))
    {
        this.proto = proto;

        // add upvalues
        for (int i = 0; i < proto.UpValues.Length; i++)
        {
            var description = proto.UpValues[i];
            var upValue = GetUpValueFromDescription(state, state.CurrentThread, environment == null ? state.EnvUpValue : UpValue.Closed(environment), description);
            upValues.Add(upValue);
        }
    }

    public Chunk Proto => proto;
    public ReadOnlySpan<UpValue> UpValues => upValues.AsSpan();
    internal Span<UpValue> GetUpValuesSpan() => upValues.AsSpan();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal LuaValue GetUpValue(int index)
    {
        return upValues[index].GetValue();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref readonly LuaValue GetUpValueRef(int index)
    {
        return ref upValues[index].GetValueRef();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetUpValue(int index, LuaValue value)
    {
        upValues[index].SetValue(value);
    }

    static UpValue GetUpValueFromDescription(LuaState state, LuaThread thread, UpValue envUpValue, UpValueInfo description)
    {
        if (description.IsInRegister)
        {
            return state.GetOrAddUpValue(thread, thread.GetCurrentFrame().Base + description.Index);
        }

        if (description.Index == -1) // -1 is global environment
        {
            return envUpValue;
        }

        if (thread.GetCurrentFrame().Function is Closure parentClosure)
        {
            return parentClosure.UpValues[description.Index];
        }

        throw new Exception();
    }
}