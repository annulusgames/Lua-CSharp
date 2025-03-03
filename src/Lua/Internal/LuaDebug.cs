using Lua.Runtime;
using static Lua.Internal.OpMode;
using static Lua.Internal.OpArgMask;

namespace Lua.Internal;

internal readonly struct LuaDebug : IDisposable
{
    readonly LuaDebugBuffer buffer;
    readonly uint version;

    LuaDebug(LuaDebugBuffer buffer, uint version)
    {
        this.buffer = buffer;
        this.version = version;
    }

    public string? Name
    {
        get
        {
            CheckVersion();
            return buffer.Name;
        }
    }

    public string? NameWhat
    {
        get
        {
            CheckVersion();
            return buffer.NameWhat;
        }
    }

    public string? What
    {
        get
        {
            CheckVersion();
            return buffer.What;
        }
    }

    public string? Source
    {
        get
        {
            CheckVersion();
            return buffer.Source;
        }
    }

    public int CurrentLine
    {
        get
        {
            CheckVersion();
            return buffer.CurrentLine;
        }
    }

    public int LineDefined
    {
        get
        {
            CheckVersion();
            return buffer.LineDefined;
        }
    }

    public int LastLineDefined
    {
        get
        {
            CheckVersion();
            return buffer.LastLineDefined;
        }
    }

    public int UpValueCount
    {
        get
        {
            CheckVersion();
            return buffer.UpValueCount;
        }
    }

    public int ParameterCount
    {
        get
        {
            CheckVersion();
            return buffer.ParameterCount;
        }
    }

    public bool IsVarArg
    {
        get
        {
            CheckVersion();
            return buffer.IsVarArg;
        }
    }

    public bool IsTailCall
    {
        get
        {
            CheckVersion();
            return buffer.IsTailCall;
        }
    }

    public ReadOnlySpan<char> ShortSource
    {
        get
        {
            CheckVersion();
            return buffer.ShortSource.AsSpan(0, buffer.ShortSourceLength);
        }
    }


    public static LuaDebug Create(LuaState state, CallStackFrame? prevFrame, CallStackFrame? frame, LuaFunction function, int pc, ReadOnlySpan<char> what, out bool isValid)
    {
        if (!state.DebugBufferPool.TryPop(out var buffer))
        {
            buffer = new(state);
        }

        isValid = buffer.GetInfo(prevFrame, frame, function, pc, what);

        return new(buffer, buffer.version);
    }

    public void CheckVersion()
    {
        if (buffer.version != version) ThrowObjectDisposedException();
    }


    public void Dispose()
    {
        if (buffer.version != version) ThrowObjectDisposedException();
        buffer.Return(version);
    }

    void ThrowObjectDisposedException()
    {
        throw new ObjectDisposedException("This has been disposed");
    }


    internal class LuaDebugBuffer(LuaState state)
    {
        internal uint version;
        LuaState state = state;
        public string? Name;
        public string? NameWhat;
        public string? What;
        public string? Source;
        public int CurrentLine;
        public int LineDefined;
        public int LastLineDefined;
        public int UpValueCount;
        public int ParameterCount;
        public bool IsVarArg;
        public bool IsTailCall;
        public readonly char[] ShortSource = new char[59];
        public int ShortSourceLength;

        internal void Return(uint version)
        {
            if (this.version != version) throw new ObjectDisposedException("Buffer has been modified");

            Name = null;
            NameWhat = null;
            What = null;
            Source = null;
            CurrentLine = 0;
            LineDefined = 0;
            LastLineDefined = 0;
            UpValueCount = 0;
            ParameterCount = 0;
            IsVarArg = false;
            IsTailCall = false;

            if (version < uint.MaxValue)
            {
                this.version++;
                state.DebugBufferPool.Push(this);
            }
        }


        internal bool GetInfo(CallStackFrame? prevFrame, CallStackFrame? frame, LuaFunction function, int pc, ReadOnlySpan<char> what)
        {
            Closure? closure = function as Closure;
            int status = 1;
            foreach (var c in what)
            {
                switch (c)
                {
                    case 'S':
                        {
                            GetFuncInfo(function);
                            break;
                        }
                    case 'l':
                        {
                            CurrentLine = (pc >= 0 && closure is not null) ? closure.Proto.SourcePositions[pc].Line : -1;
                            break;
                        }
                    case 'u':
                        {
                            UpValueCount = (closure is null) ? 0 : closure.UpValues.Length;
                            if (closure is null)
                            {
                                IsVarArg = true;
                                ParameterCount = 0;
                            }
                            else
                            {
                                IsVarArg = closure.Proto.HasVariableArguments;
                                ParameterCount = closure.Proto.ParameterCount;
                            }

                            break;
                        }
                    case 't':
                        {
                            IsTailCall = frame.HasValue && (frame.Value.Flags | CallStackFrameFlags.TailCall) == frame.Value.Flags;
                            break;
                        }
                    case 'n':
                        {
                            /* calling function is a known Lua function? */
                            if (prevFrame is { Function: Closure prevFrameClosure })
                                NameWhat = GetFuncName(prevFrameClosure.Proto, frame?.CallerInstructionIndex ?? 0, out Name);
                            else
                                NameWhat = null;
                            if (NameWhat is null)
                            {
                                NameWhat = ""; /* not found */
                                Name = null;
                            }
                            else if (NameWhat != null && Name is "?")
                            {
                                Name = function.Name;
                            }

                            break;
                        }
                    case 'L':
                    case 'f': /* handled by lua_getinfo */
                        break;
                    default:
                        status = 0; /* invalid option */
                        break;
                }
            }

            return status == 1;
        }

        void GetFuncInfo(LuaFunction f)
        {
            if (f is not Closure cl)
            {
                Source = "=[C#]";
                LineDefined = -1;
                LastLineDefined = -1;
                What = "C#";
            }
            else
            {
                var p = cl.Proto;
                Source = p.GetRoot().Name;
                LineDefined = p.LineDefined;
                LastLineDefined = p.LastLineDefined;
                What = (p.GetRoot() == p) ? "main" : "Lua";
            }

            ShortSourceLength = WriteShortSource(Source, ShortSource);
        }
    }


    internal static string? GetLocalName(Chunk chunk, int register, int pc)
    {
        var locals = chunk.Locals;
        foreach (var local in locals)
        {
            if (local.Index == register && pc >= local.StartPc && pc < local.EndPc)
            {
                return local.Name.ToString();
            }

            if (local.Index > register)
            {
                break;
            }
        }

        return null;
    }

    static int FilterPc(int pc, int jmpTarget)
    {
        if (pc < jmpTarget) /* is code conditional (inside a jump)? */
            return -1; /* cannot know who sets that register */
        else return pc; /* current position sets that register */
    }

    internal static int FindSetRegister(Chunk chunk, int lastPc, int reg)
    {
        int pc;
        int setReg = -1; /* keep last instruction that changed 'reg' */
        int jmpTarget = 0; /* any code before this address is conditional */
        var instructions = chunk.Instructions;
        for (pc = 0; pc < lastPc; pc++)
        {
            Instruction i = instructions[pc];
            OpCode op = i.OpCode;
            int a = i.A;
            switch (op)
            {
                case OpCode.LoadNil:
                    {
                        int b = i.B;
                        if (a <= reg && reg <= a + b) /* set registers from 'a' to 'a+b' */
                            setReg = FilterPc(pc, jmpTarget);
                        break;
                    }
                case OpCode.TForCall:
                    {
                        if (reg >= a + 2) /* affect all regs above its base */
                            setReg = FilterPc(pc, jmpTarget);
                        break;
                    }
                case OpCode.Call:
                case OpCode.TailCall:
                    {
                        if (reg >= a) /* affect all registers above base */
                            setReg = FilterPc(pc, jmpTarget);
                        break;
                    }
                case OpCode.Jmp:
                    {
                        int b = i.SBx;
                        int dest = pc + 1 + b;
                        /* jump is forward and do not skip `lastpc'? */
                        if (pc < dest && dest <= lastPc)
                        {
                            if (dest > jmpTarget)
                                jmpTarget = dest; /* update 'jmptarget' */
                        }

                        break;
                    }
                case OpCode.Test:
                    {
                        if (reg == a) /* jumped code can change 'a' */
                            setReg = FilterPc(pc, jmpTarget);
                        break;
                    }
                default:
                    if (TestAMode(op) && reg == a) /* any instruction that set A */
                        setReg = FilterPc(pc, jmpTarget);
                    break;
            }
        }

        return setReg;
    }

    static void GetConstantName(Chunk p, int pc, int c, out string name)
    {
        if (c >= 256)
        {
            /* is 'c' a constant? */
            ref var kvalue = ref p.Constants[c - 256];
            if (kvalue.TryReadString(out name))
            {
                /* literal constant? */
                /* it is its own name */
                return;
            }
            /* else no reasonable name found */
        }
        else
        {
            /* 'c' is a register */
            var what = GetName(p, pc, c, out name!); /* search for 'c' */
            if (what != null && what[0] == 'c')
            {
                /* found a constant name? */
                return; /* 'name' already filled */
            }
            /* else no reasonable name found */
        }

        name = "?"; /* no reasonable name found */
    }


    internal static string? GetName(Chunk chunk, int lastPc, int reg, out string? name)
    {
        name = GetLocalName(chunk, reg, lastPc);
        if (name != null)
        {
            return "local";
        }

        var pc = FindSetRegister(chunk, lastPc, reg);
        if (pc != -1)
        {
            /* could find instruction? */
            Instruction i = chunk.Instructions[pc];
            OpCode op = i.OpCode;
            switch (op)
            {
                case OpCode.Move:
                    {
                        int b = i.B; /* move from 'b' to 'a' */
                        if (b < i.A)
                            return GetName(chunk, pc, b, out name); /* get name for 'b' */
                        break;
                    }
                case OpCode.GetTabUp:
                case OpCode.GetTable:
                    {
                        int k = i.C; /* key index */
                        int t = i.B; /* table index */

                        var vn = (op == OpCode.GetTable) /* name of indexed variable */
                            ? GetLocalName(chunk, t + 1, pc)
                            : chunk.UpValues[t].Name.ToString();
                        GetConstantName(chunk, pc, k, out name);
                        return vn is "_ENV" ? "global" : "field";
                    }
                case OpCode.GetUpVal:
                    {
                        name = chunk.UpValues[i.B].Name.ToString();
                        return "upvalue";
                    }
                case OpCode.LoadK:
                case OpCode.LoadKX:
                    {
                        uint b = (op == OpCode.LoadKX)
                            ? i.Bx
                            : (chunk.Instructions[pc + 1].Ax);
                        if (chunk.Constants[b].TryReadString(out name))
                        {
                            return "constant";
                        }

                        break;
                    }
                case OpCode.Self:
                    {
                        int k = i.C; /* key index */
                        GetConstantName(chunk, pc, k, out name);
                        return "method";
                    }
                default: break; /* go through to return NULL */
            }
        }

        return null; /* could not find reasonable name */
    }

    internal static string? GetFuncName(Chunk chunk, int pc, out string? name)
    {
        Instruction i = chunk.Instructions[pc]; /* calling instruction */
        switch (i.OpCode)
        {
            case OpCode.Call:
            case OpCode.TailCall: /* get function name */
                return GetName(chunk, pc, i.A, out name);
            case OpCode.TForCall:
                {
                    /* for iterator */
                    name = "for iterator";
                    return "for iterator";
                }
            case OpCode.Self:
            case OpCode.GetTabUp:
            case OpCode.GetTable:
                name = "index";
                break;
            case OpCode.SetTabUp:
            case OpCode.SetTable:
                name = "newindex";
                break;
            case OpCode.Add:
                name = "add";
                break;
            case OpCode.Sub:
                name = "sub";
                break;
            case OpCode.Mul:
                name = "mul";
                break;
            case OpCode.Div:
                name = "div";
                break;
            case OpCode.Mod:
                name = "mod";
                break;
            case OpCode.Pow:
                name = "pow";
                break;
            case OpCode.Unm:
                name = "unm";
                break;
            case OpCode.Len:
                name = "len";
                break;
            case OpCode.Concat:
                name = "concat";
                break;
            case OpCode.Eq:
                name = "eq";
                break;
            case OpCode.Lt:
                name = "lt";
                break;
            case OpCode.Le:
                name = "le";
                break;
            default:
                name = null;
                return null;
        }

        return "metamethod";
    }

    internal static int WriteShortSource(ReadOnlySpan<char> source, Span<char> dest)
    {
        const string PRE = "[string \"";
        const int PRE_LEN = 9;
        const string POS = "\"]";
        const int POS_LEN = 2;
        const string RETS = "...";
        const int RETS_LEN = 3;
        const string PREPOS = "[string \"\"]";

        const int BUFFER_LEN = 59;
        if (dest.Length != BUFFER_LEN) throw new ArgumentException("dest must be 60 chars long");

        if (source.Length == 0)
        {
            PREPOS.AsSpan().CopyTo(dest);
            return PREPOS.Length;
        }

        if (source[0] == '=')
        {
            source = source[1..]; /* skip the '=' */
            /* 'literal' source */
            if (source.Length < BUFFER_LEN) /* small enough? */
            {
                source.CopyTo(dest);
                return source.Length;
            }
            else
            {
                /* truncate it */
                source[..BUFFER_LEN].CopyTo(dest);
                return BUFFER_LEN;
            }
        }
        else if (source[0] == '@')
        {
            /* file name */
            source = source[1..]; /* skip the '@' */
            if (source.Length <= BUFFER_LEN) /* small enough? */
            {
                source.CopyTo(dest);
                return source.Length;
            }
            else
            {
                /* add '...' before rest of name */
                RETS.AsSpan().CopyTo(dest);
                source[^(BUFFER_LEN - RETS_LEN)..].CopyTo(dest[RETS_LEN..]);

                return BUFFER_LEN;
            }
        }
        else
        {
            /* string; format as [string "source"] */


            PRE.AsSpan().CopyTo(dest);
            int newLine = source.IndexOf('\n');
            if (newLine == -1 && source.Length < BUFFER_LEN - (PRE_LEN + RETS_LEN + POS_LEN))
            {
                source.CopyTo(dest[PRE_LEN..]);
                POS.AsSpan().CopyTo(dest[(PRE_LEN + source.Length)..]);
                return PRE_LEN + source.Length + POS_LEN;
            }

            if (newLine != -1)
            {
                source = source[..newLine]; /* stop at first newline */
            }

            if (BUFFER_LEN - (PRE_LEN + RETS_LEN + POS_LEN) < source.Length)
            {
                source = source[..(BUFFER_LEN - PRE_LEN - RETS_LEN - POS_LEN)];
            }

            /* add '...' before rest of name */
            source.CopyTo(dest[PRE_LEN..]);
            RETS.AsSpan().CopyTo(dest[(PRE_LEN + source.Length)..]);
            POS.AsSpan().CopyTo(dest[(PRE_LEN + source.Length + RETS_LEN)..]);
            return PRE_LEN + source.Length + RETS_LEN + POS_LEN;
        }
    }

    static int GetOpMode(byte t, byte a, OpArgMask b, OpArgMask c, OpMode m) => (((t) << 7) | ((a) << 6) | (((byte)b) << 4) | (((byte)c) << 2) | ((byte)m));


    static readonly int[] OpModes =
    [
        GetOpMode(0, 1, OpArgR, OpArgN, iABC), /* OP_MOVE */
        GetOpMode(0, 1, OpArgK, OpArgN, iABx), /* OP_LOADK */
        GetOpMode(0, 1, OpArgN, OpArgN, iABx), /* OP_LOADKX */
        GetOpMode(0, 1, OpArgU, OpArgU, iABC), /* OP_LOADBOOL */
        GetOpMode(0, 1, OpArgU, OpArgN, iABC), /* OP_LOADNIL */
        GetOpMode(0, 1, OpArgU, OpArgN, iABC), /* OP_GETUPVAL */
        GetOpMode(0, 1, OpArgU, OpArgK, iABC), /* OP_GETTABUP */
        GetOpMode(0, 1, OpArgR, OpArgK, iABC), /* OP_GETTABLE */
        GetOpMode(0, 0, OpArgK, OpArgK, iABC), /* OP_SETTABUP */
        GetOpMode(0, 0, OpArgU, OpArgN, iABC), /* OP_SETUPVAL */
        GetOpMode(0, 0, OpArgK, OpArgK, iABC), /* OP_SETTABLE */
        GetOpMode(0, 1, OpArgU, OpArgU, iABC), /* OP_NEWTABLE */
        GetOpMode(0, 1, OpArgR, OpArgK, iABC), /* OP_SELF */
        GetOpMode(0, 1, OpArgK, OpArgK, iABC), /* OP_ADD */
        GetOpMode(0, 1, OpArgK, OpArgK, iABC), /* OP_SUB */
        GetOpMode(0, 1, OpArgK, OpArgK, iABC), /* OP_MUL */
        GetOpMode(0, 1, OpArgK, OpArgK, iABC), /* OP_DIV */
        GetOpMode(0, 1, OpArgK, OpArgK, iABC), /* OP_MOD */
        GetOpMode(0, 1, OpArgK, OpArgK, iABC), /* OP_POW */
        GetOpMode(0, 1, OpArgR, OpArgN, iABC), /* OP_UNM */
        GetOpMode(0, 1, OpArgR, OpArgN, iABC), /* OP_NOT */
        GetOpMode(0, 1, OpArgR, OpArgN, iABC), /* OP_LEN */
        GetOpMode(0, 1, OpArgR, OpArgR, iABC), /* OP_CONCAT */
        GetOpMode(0, 0, OpArgR, OpArgN, iAsBx), /* OP_JMP */
        GetOpMode(1, 0, OpArgK, OpArgK, iABC), /* OP_EQ */
        GetOpMode(1, 0, OpArgK, OpArgK, iABC), /* OP_LT */
        GetOpMode(1, 0, OpArgK, OpArgK, iABC), /* OP_LE */
        GetOpMode(1, 0, OpArgN, OpArgU, iABC), /* OP_TEST */
        GetOpMode(1, 1, OpArgR, OpArgU, iABC), /* OP_TESTSET */
        GetOpMode(0, 1, OpArgU, OpArgU, iABC), /* OP_CALL */
        GetOpMode(0, 1, OpArgU, OpArgU, iABC), /* OP_TAILCALL */
        GetOpMode(0, 0, OpArgU, OpArgN, iABC), /* OP_RETURN */
        GetOpMode(0, 1, OpArgR, OpArgN, iAsBx), /* OP_FORLOOP */
        GetOpMode(0, 1, OpArgR, OpArgN, iAsBx), /* OP_FORPREP */
        GetOpMode(0, 0, OpArgN, OpArgU, iABC), /* OP_TFORCALL */
        GetOpMode(0, 1, OpArgR, OpArgN, iAsBx), /* OP_TFORLOOP */
        GetOpMode(0, 0, OpArgU, OpArgU, iABC), /* OP_SETLIST */
        GetOpMode(0, 1, OpArgU, OpArgN, iABx), /* OP_CLOSURE */
        GetOpMode(0, 1, OpArgU, OpArgN, iABC), /* OP_VARARG */
        GetOpMode(0, 0, OpArgU, OpArgU, iAx), /* OP_EXTRAARG */
    ];

    internal static OpMode GetOpMode(OpCode m) => (OpMode)(OpModes[(int)m] & 3);
    internal static OpArgMask GetBMode(OpCode m) => (OpArgMask)((OpModes[(int)m] >> 4) & 3);
    internal static OpArgMask GetCMode(OpCode m) => (OpArgMask)((OpModes[(int)m] >> 2) & 3);
    internal static bool TestAMode(OpCode m) => (OpModes[(int)m] & (1 << 6)) != 0;
    internal static bool TestTMode(OpCode m) => (OpModes[(int)m] & (1 << 7)) != 0;
}

internal enum OpMode : byte
{
    iABC,
    iABx,
    iAsBx,
    iAx
}

internal enum OpArgMask : byte
{
    OpArgN, /* argument is not used */
    OpArgU, /* argument is used */
    OpArgR, /* argument is a register or a jump offset */
    OpArgK /* argument is a constant or register/constant */
}