using System.Runtime.CompilerServices;

namespace Lua.Runtime;

internal static class LuaRuntimeExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetMetamethod(this LuaValue value, LuaState state, string methodName, out LuaValue result)
    {
        result = default;
        return state.TryGetMetatable(value, out var metatable) &&
            metatable.TryGetValue(methodName, out result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetVariableArgumentCount(this LuaFunction function, int argumentCount)
    {
        return function is Closure { Proto.HasVariableArguments: true } luaClosure
            ?argumentCount - luaClosure.Proto.ParameterCount
            : 0;
    }
}