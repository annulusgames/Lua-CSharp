using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lua.Internal;

[StructLayout(LayoutKind.Auto)]
public struct FastStackCore<T>
{
    const int InitialCapacity = 8;

    T?[] array;
    int tail;

    public int Count => tail;

    public readonly ReadOnlySpan<T> AsSpan()
    {
        if (array == null) return [];
        return array.AsSpan(0, tail)!;
    }

    public readonly Span<T?> GetBuffer()
    {
        if (array == null) return [];
        return array.AsSpan();
    }

    public readonly T? this[int index]
    {
        get
        {
            return array[index];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(in T item)
    {
        array ??= new T[InitialCapacity];

        if (tail == array.Length)
        {
            Array.Resize(ref array, tail * 2);
        }

        array[tail] = item;
        tail++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPop(out T value)
    {
        if (tail == 0)
        {
            value = default!;
            return false;
        }

        tail--;
        value = array[tail]!;
        array[tail] = default;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryPop()
    {
        if (tail == 0)
        {
            return false;
        }
        array[--tail] = default;

        return true;
    }

    public T Pop()
    {
        if (!TryPop(out var result)) ThrowForEmptyStack();
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPeek(out T value)
    {
        if (tail == 0)
        {
            value = default!;
            return false;
        }

        value = array[tail - 1]!;
        return true;
    }

    public T Peek()
    {
        if (!TryPeek(out var result)) ThrowForEmptyStack();
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref T PeekRef()
    {
        if (tail == 0)
        {
            ThrowForEmptyStack();
        }

        return ref array[tail - 1]!;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureCapacity(int capacity)
    {
        if (array == null)
        {
            array = new T[InitialCapacity];
        }

        var newSize = array.Length;
        while (newSize < capacity)
        {
            newSize *= 2;
        }

        Array.Resize(ref array, newSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void NotifyTop(int top)
    {
        if (tail < top) tail = top;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        array.AsSpan(0, tail).Clear();
        tail = 0;
    }

    [DoesNotReturn]
    void ThrowForEmptyStack()
    {
        throw new InvalidOperationException("Empty stack");
    }
}