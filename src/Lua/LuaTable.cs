using System.Runtime.CompilerServices;
using Lua.Internal;

namespace Lua;

public sealed class LuaTable
{
    public LuaTable() : this(8, 8)
    {
    }

    public LuaTable(int arrayCapacity, int dictionaryCapacity)
    {
        array = new LuaValue[arrayCapacity];
        dictionary = new(dictionaryCapacity);
    }

    LuaValue[] array;
    readonly LuaValueDictionary dictionary;
    LuaTable? metatable;

    internal LuaValueDictionary Dictionary => dictionary;

    public LuaValue this[LuaValue key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (key.Type is LuaValueType.Nil) ThrowIndexIsNil();

            if (TryGetInteger(key, out var index))
            {
                if (index > 0 && index <= array.Length)
                {
                    // Arrays in Lua are 1-origin...
                    return array[index - 1];
                }
            }

            if (dictionary.TryGetValue(key, out var value)) return value;
            return LuaValue.Nil;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (key.TryReadNumber(out var d))
            {
                if (double.IsNaN(d))
                {
                    ThrowIndexIsNaN();
                }

                if (MathEx.IsInteger(d))
                {
                    var index = (int)d;
                    if (0 < index && index <= Math.Max(array.Length * 2, 8))
                    {
                        if (array.Length < index)
                            EnsureArrayCapacity(index);
                        array[index - 1] = value;
                        return;
                    }
                }
            }

            dictionary[key] = value;
        }
    }

    public int HashMapCount
    {
        get => dictionary.Count - dictionary.NilCount;
    }

    public int ArrayLength
    {
        get
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i].Type is LuaValueType.Nil) return i;
            }

            return array.Length;
        }
    }

    public LuaTable? Metatable
    {
        get => metatable;
        set => metatable = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(LuaValue key, out LuaValue value)
    {
        if (key.Type is LuaValueType.Nil)
        {
            value = default;
            return false;
        }

        if (TryGetInteger(key, out var index))
        {
            if (index > 0 && index <= array.Length)
            {
                value = array[index - 1];
                return value.Type is not LuaValueType.Nil;
            }
        }

        return dictionary.TryGetValue(key, out value) && value.Type is not LuaValueType.Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref LuaValue FindValue(LuaValue key)
    {
        if (key.Type is LuaValueType.Nil)
        {
            ThrowIndexIsNil();
        }

        if (TryGetInteger(key, out var index))
        {
            if (index > 0 && index <= array.Length)
            {
                return ref array[index - 1];
            }
        }

        return ref dictionary.FindValue(key, out _);
    }

    public bool ContainsKey(LuaValue key)
    {
        if (key.Type is LuaValueType.Nil)
        {
            return false;
        }

        if (TryGetInteger(key, out var index))
        {
            return index > 0 && index <= array.Length &&
                   array[index - 1].Type != LuaValueType.Nil;
        }

        return dictionary.TryGetValue(key, out var value) && value.Type is not LuaValueType.Nil;
    }

    public LuaValue RemoveAt(int index)
    {
        var arrayIndex = index - 1;
        var value = array[arrayIndex];

        if (arrayIndex < array.Length - 1)
        {
            array.AsSpan(arrayIndex + 1).CopyTo(array.AsSpan(arrayIndex));
        }

        array[^1] = default;

        return value;
    }

    public void Insert(int index, LuaValue value)
    {
        if (index <= 0 || index > array.Length + 1)
        {
            throw new IndexOutOfRangeException();
        }

        var arrayIndex = index - 1;

        if (index > array.Length || array[^1].Type != LuaValueType.Nil)
        {
            EnsureArrayCapacity(array.Length + 1);
        }

        if (arrayIndex != array.Length - 1)
        {
            array.AsSpan(arrayIndex, array.Length - arrayIndex - 1).CopyTo(array.AsSpan(arrayIndex + 1));
        }

        array[arrayIndex] = value;
    }

    public bool TryGetNext(LuaValue key, out KeyValuePair<LuaValue, LuaValue> pair)
    {
        var index = -1;
        if (key.Type is LuaValueType.Nil)
        {
            index = 0;
        }
        else if (TryGetInteger(key, out var integer) && integer > 0 && integer <= array.Length)
        {
            index = integer;
        }

        if (index != -1)
        {
            var span = array.AsSpan(index);
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Type is not LuaValueType.Nil)
                {
                    pair = new(index + i + 1, span[i]);
                    return true;
                }
            }

            foreach (var kv in dictionary)
            {
                if (kv.Value.Type is not LuaValueType.Nil)
                {
                    pair = kv;
                    return true;
                }
            }
        }
        else
        {
            if (dictionary.TryGetNext(key, out pair))
            {
                return true;
            }
        }

        pair = default;
        return false;
    }

    public void Clear()
    {
        dictionary.Clear();
    }

    public Memory<LuaValue> GetArrayMemory()
    {
        return array.AsMemory();
    }

    public Span<LuaValue> GetArraySpan()
    {
        return array.AsSpan();
    }

    internal void EnsureArrayCapacity(int newCapacity)
    {
        if (array.Length >= newCapacity) return;

        var prevLength = array.Length;
        var newLength = array.Length;
        if (newLength == 0) newLength = 8;

        while (newLength < newCapacity)
        {
            newLength *= 2;
        }

        Array.Resize(ref array, newLength);

        using var indexList = new PooledList<(int, LuaValue)>(dictionary.Count);

        // Move some of the elements of the hash part to a newly allocated array
        foreach (var kv in dictionary)
        {
            if (TryGetInteger(kv.Key, out var index))
            {
                if (index > prevLength && index <= newLength)
                {
                    indexList.Add((index, kv.Value));
                }
            }
        }

        foreach ((var index, var value) in indexList.AsSpan())
        {
            dictionary.Remove(index);
            array[index - 1] = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool TryGetInteger(LuaValue value, out int integer)
    {
        if (value.TryReadNumber(out var num) && MathEx.IsInteger(num))
        {
            integer = (int)num;
            return true;
        }

        integer = default;
        return false;
    }

    static void ThrowIndexIsNil()
    {
        throw new ArgumentException("the table index is nil");
    }

    static void ThrowIndexIsNaN()
    {
        throw new ArgumentException("the table index is NaN");
    }
}