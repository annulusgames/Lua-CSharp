using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Lua.Runtime;
using Lua.Internal;

namespace Lua.CodeAnalysis.Compilation;

public class FunctionCompilationContext : IDisposable
{
    static class Pool
    {
        static readonly ConcurrentStack<FunctionCompilationContext> stack = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FunctionCompilationContext Rent()
        {
            if (!stack.TryPop(out var context))
            {
                context = new();
            }

            return context;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(FunctionCompilationContext context)
        {
            context.Reset();
            stack.Push(context);
        }
    }

    internal static FunctionCompilationContext Create(ScopeCompilationContext? parentScope)
    {
        var context = Pool.Rent();
        context.ParentScope = parentScope;
        return context;
    }

    FunctionCompilationContext()
    {
        Scope = new()
        {
            Function = this
        };
    }

    // instructions
    FastListCore<Instruction> instructions;
    FastListCore<SourcePosition> instructionPositions;

    // constants
    Dictionary<LuaValue, int> constantIndexMap = new(16);
    FastListCore<LuaValue> constants;

    // functions
    Dictionary<ReadOnlyMemory<char>, int> functionMap = new(32, Utf16StringMemoryComparer.Default);
    FastListCore<Chunk> functions;

    // upvalues
    FastListCore<UpValueInfo> upvalues;
    FastListCore<LocalValueInfo> localVariables;

    // loop
    FastListCore<BreakDescription> breakQueue;
    FastListCore<GotoDescription> gotoQueue;

    /// <summary>
    /// Maximum local stack size
    /// </summary>
    public byte MaxStackPosition { get; set; }

    /// <summary>
    /// Chunk name (for debug)
    /// </summary>
    public string? ChunkName { get; set; }

    /// <summary>
    /// Level of nesting of while, repeat, and for loops
    /// </summary>
    public int LoopLevel { get; set; }

    /// <summary>
    /// Number of parameters
    /// </summary>
    public int ParameterCount { get; set; }

    /// <summary>
    /// Weather the function has variable arguments
    /// </summary>
    public bool HasVariableArguments { get; set; }

    /// <summary>
    /// Line number where the function is defined
    /// </summary>
    public int LineDefined { get; set; }

    /// <summary>
    /// Last line number where the function is defined
    /// </summary>
    public int LastLineDefined { get; set; }

    /// <summary>
    /// Parent scope context
    /// </summary>
    public ScopeCompilationContext? ParentScope { get; private set; }

    /// <summary>
    /// Top-level scope context
    /// </summary>
    public ScopeCompilationContext Scope { get; }

    /// <summary>
    /// Instructions
    /// </summary>
    public Span<Instruction> Instructions => instructions.AsSpan();

    /// <summary>
    /// Push the new instruction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushInstruction(in Instruction instruction, in SourcePosition position)
    {
        instructions.Add(instruction);
        instructionPositions.Add(position);
    }

    /// <summary>
    /// Push or merge the new instruction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushOrMergeInstruction(int lastLocal, in Instruction instruction, in SourcePosition position, ref bool incrementStackPosition)
    {
        if (instructions.Length == 0)
        {
            instructions.Add(instruction);
            instructionPositions.Add(position);
            return;
        }

        ref var lastInstruction = ref instructions.AsSpan()[^1];
        var opcode = instruction.OpCode;
        switch (opcode)
        {
            case OpCode.Move:
                // last A is not local variable
                if (lastInstruction.A != lastLocal &&
                    // available to merge
                    lastInstruction.A == instruction.B &&
                    // not already merged
                    lastInstruction.A != lastInstruction.B)
                {
                    switch (lastInstruction.OpCode)
                    {
                        case OpCode.GetTable:
                        case OpCode.Add:
                        case OpCode.Sub:
                        case OpCode.Mul:
                        case OpCode.Div:
                        case OpCode.Mod:
                        case OpCode.Pow:
                        case OpCode.Concat:
                            {
                                lastInstruction.A = instruction.A;
                                incrementStackPosition = false;
                                return;
                            }
                    }
                }

                break;
            case OpCode.GetTable:
                {
                    // Merge MOVE GetTable
                    if (lastInstruction.OpCode == OpCode.Move && lastLocal != lastInstruction.A)
                    {
                        if (lastInstruction.A == instruction.B)
                        {
                            lastInstruction = Instruction.GetTable(instruction.A, lastInstruction.B, instruction.C);
                            instructionPositions[^1] = position;
                            incrementStackPosition = false;
                            return;
                        }
                    }

                    break;
                }
            case OpCode.SetTable:
                {
                    // Merge MOVE SETTABLE
                    if (lastInstruction.OpCode == OpCode.Move && lastLocal != lastInstruction.A)
                    {
                        var lastB = lastInstruction.B;
                        var lastA = lastInstruction.A;
                        if (lastB < 255 && lastA == instruction.A)
                        {
                            // Merge MOVE MOVE SETTABLE
                            if (instructions.Length > 2)
                            {
                                ref var last2Instruction = ref instructions.AsSpan()[^2];
                                var last2A = last2Instruction.A;
                                if (last2Instruction.OpCode == OpCode.Move && lastLocal != last2A && instruction.C == last2A)
                                {
                                    last2Instruction = Instruction.SetTable((byte)(lastB), instruction.B, last2Instruction.B);
                                    instructions.RemoveAtSwapback(instructions.Length - 1);
                                    instructionPositions.RemoveAtSwapback(instructionPositions.Length - 1);
                                    instructionPositions[^1] = position;
                                    incrementStackPosition = false;
                                    return;
                                }
                            }

                            lastInstruction = Instruction.SetTable((byte)(lastB), instruction.B, instruction.C);
                            instructionPositions[^1] = position;
                            incrementStackPosition = false;
                            return;
                        }

                        if (lastA == instruction.C)
                        {
                            lastInstruction = Instruction.SetTable(instruction.A, instruction.B, lastB);
                            instructionPositions[^1] = position;
                            incrementStackPosition = false;
                            return;
                        }
                    }
                    else if (lastInstruction.OpCode == OpCode.GetTabUp && instructions.Length >= 2)
                    {
                        ref var last2Instruction = ref instructions[^2];
                        var last2OpCode = last2Instruction.OpCode;
                        if (last2OpCode is OpCode.LoadK or OpCode.Move)
                        {
                            var last2A = last2Instruction.A;
                            if (last2A != lastLocal && instruction.C == last2A)
                            {
                                var c = last2OpCode == OpCode.LoadK ? last2Instruction.Bx + 256 : last2Instruction.B;
                                last2Instruction = lastInstruction;
                                lastInstruction = instruction with { C = (ushort)c };
                                instructionPositions[^2] = instructionPositions[^1];
                                instructionPositions[^1] = position;
                                incrementStackPosition = false;
                                return;
                            }
                        }
                    }

                    break;
                }
            case OpCode.Unm:
            case OpCode.Not:
            case OpCode.Len:
                if (lastInstruction.OpCode == OpCode.Move && lastLocal != lastInstruction.A && lastInstruction.A == instruction.B)
                {
                    lastInstruction = instruction with { B = lastInstruction.B };
                    ;
                    instructionPositions[^1] = position;
                    incrementStackPosition = false;
                    return;
                }

                break;
            case OpCode.Return:
                if (lastInstruction.OpCode == OpCode.Move && instruction.B == 2 && lastInstruction.B < 256)
                {
                    lastInstruction = instruction with { A = (byte)lastInstruction.B };
                    instructionPositions[^1] = position;
                    incrementStackPosition = false;
                    return;
                }

                break;
        }

        instructions.Add(instruction);
        instructionPositions.Add(position);
    }

    /// <summary>
    /// Gets the index of the constant from the value, or if the constant is not registered it is added and its index is returned.
    /// </summary>
    public uint GetConstantIndex(in LuaValue value)
    {
        if (!constantIndexMap.TryGetValue(value, out var index))
        {
            index = constants.Length;

            constants.Add(value);
            constantIndexMap.Add(value, index);
        }

        return (uint)index;
    }

    public void AddOrSetFunctionProto(ReadOnlyMemory<char> name, Chunk chunk, out int index)
    {
        index = functions.Length;
        functionMap[name] = functions.Length;
        functions.Add(chunk);
    }

    public void AddFunctionProto(Chunk chunk, out int index)
    {
        index = functions.Length;
        functions.Add(chunk);
    }

    public bool TryGetFunctionProto(ReadOnlyMemory<char> name, [NotNullWhen(true)] out Chunk? proto)
    {
        if (functionMap.TryGetValue(name, out var index))
        {
            proto = functions[index];
            return true;
        }
        else
        {
            proto = null;
            return false;
        }
    }

    public void AddLocalVariable(ReadOnlyMemory<char> name, LocalVariableDescription description)
    {
        localVariables.Add(new LocalValueInfo()
        {
            Name = name,
            Index = description.RegisterIndex,
            StartPc = description.StartPc,
            EndPc = Instructions.Length,
        });
    }

    public void AddUpValue(UpValueInfo upValue)
    {
        upvalues.Add(upValue);
    }

    public bool TryGetUpValue(ReadOnlyMemory<char> name, out UpValueInfo description)
    {
        var span = upvalues.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            var info = span[i];
            if (info.Name.Span.SequenceEqual(name.Span))
            {
                description = info;
                return true;
            }
        }

        if (ParentScope == null)
        {
            description = default;
            return false;
        }

        if (ParentScope.TryGetLocalVariable(name, out var localVariable))
        {
            ParentScope.HasCapturedLocalVariables = true;

            description = new()
            {
                Name = name,
                Index = localVariable.RegisterIndex,
                Id = upvalues.Length,
                IsInRegister = true,
            };
            upvalues.Add(description);

            return true;
        }
        else if (ParentScope.Function.TryGetUpValue(name, out var parentUpValue))
        {
            description = new()
            {
                Name = name,
                Index = parentUpValue.Id,
                Id = upvalues.Length,
                IsInRegister = false,
            };
            upvalues.Add(description);

            return true;
        }

        description = default;
        return false;
    }

    public void AddUnresolvedBreak(BreakDescription description, SourcePosition sourcePosition)
    {
        if (LoopLevel == 0)
        {
            LuaParseException.BreakNotInsideALoop(ChunkName, sourcePosition);
        }

        breakQueue.Add(description);
    }

    public void ResolveAllBreaks(byte startPosition, int endPosition, ScopeCompilationContext loopScope)
    {
        foreach (var description in breakQueue.AsSpan())
        {
            ref var instruction = ref Instructions[description.Index];
            if (loopScope.HasCapturedLocalVariables)
            {
                instruction.A = startPosition;
            }

            instruction.SBx = endPosition - description.Index;
        }

        breakQueue.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddUnresolvedGoto(GotoDescription description)
    {
        gotoQueue.Add(description);
    }

    public void ResolveGoto(LabelDescription labelDescription)
    {
        for (int i = 0; i < gotoQueue.Length; i++)
        {
            var gotoDesc = gotoQueue[i];
            if (gotoDesc.Name.Span.SequenceEqual(labelDescription.Name.Span))
            {
                instructions[gotoDesc.JumpInstructionIndex] = Instruction.Jmp(labelDescription.RegisterIndex, labelDescription.Index - gotoDesc.JumpInstructionIndex - 1);
                gotoQueue.RemoveAtSwapback(i);
                i--;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Chunk ToChunk()
    {
        // add return
        instructions.Add(Instruction.Return(0, 1));
        instructionPositions.Add( new (LastLineDefined, 0));
        Scope.RegisterLocalsToFunction();
        var locals = localVariables.AsSpan().ToArray();
        Array.Sort(locals, (x, y) => x.Index.CompareTo(y.Index));
        var chunk = new Chunk()
        {
            Name = ChunkName ?? "chunk",
            Instructions = instructions.AsSpan().ToArray(),
            SourcePositions = instructionPositions.AsSpan().ToArray(),
            Constants = constants.AsSpan().ToArray(),
            UpValues = upvalues.AsSpan().ToArray(),
            Locals = locals,
            Functions = functions.AsSpan().ToArray(),
            ParameterCount = ParameterCount,
            HasVariableArguments = HasVariableArguments,
            MaxStackPosition = MaxStackPosition,
            LineDefined = LineDefined,
            LastLineDefined = LastLineDefined,
        };

        foreach (var function in functions.AsSpan())
        {
            function.Parent = chunk;
        }

        return chunk;
    }

    /// <summary>
    /// Resets the values ​​held in the context.
    /// </summary>
    public void Reset()
    {
        Scope.Reset();
        instructions.Clear();
        instructionPositions.Clear();
        constantIndexMap.Clear();
        constants.Clear();
        upvalues.Clear();
        localVariables.Clear();
        functionMap.Clear();
        functions.Clear();
        breakQueue.Clear();
        gotoQueue.Clear();
        ChunkName = null;
        LoopLevel = 0;
        ParameterCount = 0;
        HasVariableArguments = false;
        MaxStackPosition = 0;
    }

    /// <summary>
    /// Returns the context object to the pool.
    /// </summary>
    public void Dispose()
    {
        ParentScope = null;
        Pool.Return(this);
    }
}