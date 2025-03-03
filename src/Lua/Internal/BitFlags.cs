namespace Lua.Internal;

internal struct BitFlags2
{
    public byte Value;

    public bool Flag0
    {
        get => (Value & 1) == 1;
        set
        {
            if (value)
            {
                Value |= 1;
            }
            else
            {
                Value = (byte)(Value & ~1);
            }
        }
    }
    
    public bool Flag1
    {
        get => (Value & 2) == 2;
        set
        {
            if (value)
            {
                Value |= 2;
            }
            else
            {
                Value = (byte)(Value & ~2);
            }
        }
    }
}