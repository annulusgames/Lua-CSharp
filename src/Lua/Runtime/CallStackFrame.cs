using System.Runtime.InteropServices;

namespace Lua.Runtime;

[StructLayout(LayoutKind.Auto)]
public record struct CallStackFrame
{
    public required int Base;
    public required LuaFunction Function;
    public required int VariableArgumentCount;
    public int CallerInstructionIndex;
    internal CallStackFrameFlags Flags;
    internal bool IsTailCall => (Flags & CallStackFrameFlags.TailCall) ==CallStackFrameFlags.TailCall;
}

[Flags]
public enum CallStackFrameFlags
{
    //None = 0,
    ReversedLe  = 1,
    TailCall = 2,
    InHook = 4,
}