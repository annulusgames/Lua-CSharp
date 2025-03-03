namespace Lua.Runtime;

public readonly record struct LocalValueInfo
{
    public required ReadOnlyMemory<char> Name { get; init; }
    public required byte Index { get; init; }
    public required int StartPc { get; init; }
    public required int EndPc { get; init; }
}