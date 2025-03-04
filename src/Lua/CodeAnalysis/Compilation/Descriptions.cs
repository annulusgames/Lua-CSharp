using Lua.Internal;
using Lua.Runtime;

namespace Lua.CodeAnalysis.Compilation
{
    public readonly record struct LocalVariableDescription
    {
        public required byte RegisterIndex { get; init; }
        public required int StartPc { get; init; }
    }

    public readonly record struct FunctionDescription
    {
        public required int Index { get; init; }
        public required int? ReturnValueCount { get; init; }
        public required Chunk Chunk { get; init; }
    }

    public readonly record struct LabelDescription
    {
        public required ReadOnlyMemory<char> Name { get; init; }
        public required int Index { get; init; }
        public required byte RegisterIndex { get; init; }
    }

    public readonly record struct GotoDescription
    {
        public required ReadOnlyMemory<char> Name { get; init; }
        public required int JumpInstructionIndex { get; init; }
    }

    public record struct BreakDescription
    {
        public required int Index { get; set; }
    }
}