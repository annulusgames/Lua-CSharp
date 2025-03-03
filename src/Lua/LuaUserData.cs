namespace Lua;

internal sealed class LuaUserData : ILuaUserData
{
    public LuaTable? Metatable { get; set; }
    readonly LuaValue[] userValues = new LuaValue[1];
    public Span<LuaValue> UserValues => userValues;

    public LuaUserData(LuaValue value, LuaTable? metatable)
    {
        userValues[0] = value;
        Metatable = metatable;
    }
}

public interface ILuaUserData
{
    LuaTable? Metatable { get; set; }

    //We use span for compatibility with lua5.4.
    Span<LuaValue> UserValues => default;
}