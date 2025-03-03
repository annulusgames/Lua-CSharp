using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Lua.Internal;
using Lua.Runtime;

namespace Lua.CodeAnalysis.Compilation;

public class ScopeCompilationContext : IDisposable
{
    static class Pool
    {
        static ConcurrentStack<ScopeCompilationContext> stack = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ScopeCompilationContext Rent()
        {
            if (!stack.TryPop(out var context))
            {
                context = new();
            }

            return context;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Return(ScopeCompilationContext context)
        {
            context.Reset();
            stack.Push(context);
        }
    }

    readonly Dictionary<ReadOnlyMemory<char>, LocalVariableDescription> localVariables = new(256, Utf16StringMemoryComparer.Default);
    readonly Dictionary<ReadOnlyMemory<char>, LabelDescription> labels = new(32, Utf16StringMemoryComparer.Default);

    byte lastLocalVariableIndex;

    public byte StackStartPosition { get; private set; }
    public byte StackPosition { get; set; }

    public byte StackTopPosition
    {
        get => (byte)(StackPosition - 1);
    }

    public bool HasCapturedLocalVariables { get; internal set; }

    /// <summary>
    /// Function context
    /// </summary>
    public FunctionCompilationContext Function { get; internal set; } = default!;

    /// <summary>
    /// Parent scope context
    /// </summary>
    public ScopeCompilationContext? Parent { get; private set; }

    public ScopeCompilationContext CreateChildScope()
    {
        var childScope = Pool.Rent();
        childScope.Parent = this;
        childScope.Function = Function;
        childScope.StackStartPosition = StackPosition;
        childScope.StackPosition = StackPosition;
        return childScope;
    }

    public FunctionCompilationContext CreateChildFunction()
    {
        var context = FunctionCompilationContext.Create(this);
        return context;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushInstruction(in Instruction instruction, SourcePosition position, bool incrementStackPosition = false)
    {
        Function.PushOrMergeInstruction(lastLocalVariableIndex, instruction, position, ref incrementStackPosition);
        if (incrementStackPosition)
        {
            StackPosition++;
        }
        Function.MaxStackPosition = Math.Max(Function.MaxStackPosition, StackPosition);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TryPushCloseUpValue(byte top, SourcePosition position)
    {
        if (HasCapturedLocalVariables && top != 0)
        {
            Function.PushInstruction(Instruction.Jmp(top, 0), position);
        }
    }

    /// <summary>
    /// Add new local variable.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddLocalVariable(ReadOnlyMemory<char> name, LocalVariableDescription description, bool markAsLastLocalVariable = true)
    {
        localVariables[name] = description;
        lastLocalVariableIndex = description.RegisterIndex;
    }


    /// <summary>
    /// Gets the local variable in scope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetLocalVariable(ReadOnlyMemory<char> name, out LocalVariableDescription description)
    {
        if (localVariables.TryGetValue(name, out description)) return true;

        // Find local variables defined in the same function
        if (Parent != null)
        {
            return Parent.TryGetLocalVariable(name, out description);
        }

        return false;
    }

    /// <summary>
    /// Gets the local variable in this scope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetLocalVariableInThisScope(ReadOnlyMemory<char> name, out LocalVariableDescription description)
    {
        return localVariables.TryGetValue(name, out description);
    }

    /// <summary>
    /// Add new label.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddLabel(LabelDescription description)
    {
        labels.Add(description.Name, description);
    }

    /// <summary>
    /// Gets the label in scope.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetLabel(ReadOnlyMemory<char> name, out LabelDescription description)
    {
        if (labels.TryGetValue(name, out description)) return true;

        // Find labels defined in the same function
        if (Parent != null)
        {
            return Parent.TryGetLabel(name, out description);
        }

        return false;
    }
    
    public void RegisterLocalsToFunction()
    {
        foreach (var localVariable in localVariables)
        {
            Function.AddLocalVariable(localVariable.Key, localVariable.Value);
        }
    }

    /// <summary>
    /// Resets the values ​​held in the context.
    /// </summary>
    public void Reset()
    {
        Parent = null;
        StackStartPosition = 0;
        StackPosition = 0;
        HasCapturedLocalVariables = false;
        localVariables.Clear();
        labels.Clear();
        lastLocalVariableIndex = 0;
    }

    /// <summary>
    /// Returns the context object to the pool.
    /// </summary>
    public void Dispose()
    {
        RegisterLocalsToFunction();
        Function = null!;
        Pool.Return(this);
    }
}