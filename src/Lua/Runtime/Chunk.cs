using Lua.CodeAnalysis;

namespace Lua.Runtime;

public sealed class Chunk
{
    public Chunk? Parent { get; internal set; }

    public required string Name { get; init; }
    public required Instruction[] Instructions { get; init; }
    public required SourcePosition[] SourcePositions { get; init; }
    public required LuaValue[] Constants { get; init; }
    public required UpValueInfo[] UpValues { get; init; }
    public required LocalValueInfo[] Locals { get; init; }
    public required Chunk[] Functions { get; init; }
    public required int ParameterCount { get; init; }
    public required bool HasVariableArguments { get; init; }
    public required byte MaxStackPosition { get; init; }
    public required int LineDefined { get; init; }
    public required int LastLineDefined { get; init; }

    Chunk? rootCache;

    internal Chunk GetRoot()
    {
        if (rootCache != null) return rootCache;
        if (Parent == null) return rootCache = this;
        return rootCache = Parent.GetRoot();
    }
}