using Lua.Runtime;
using Lua.Standard.Internal;

namespace Lua.Standard;

// TODO: optimize (remove StreamReader/Writer)

public class FileHandle : ILuaUserData
{
    public static readonly LuaFunction IndexMetamethod = new("index", (context, buffer, ct) =>
    {
        context.GetArgument<FileHandle>(0);
        var key = context.GetArgument(1);

        if (key.TryRead<string>(out var name))
        {
            buffer.Span[0] = name switch
            {
                "close" => CloseFunction!,
                "flush" => FlushFunction!,
                "lines" => LinesFunction!,
                "read" => ReadFunction!,
                "seek" => SeekFunction!,
                "setvbuf" => SetVBufFunction!,
                "write" => WriteFunction!,
                _ => LuaValue.Nil,
            };
        }
        else
        {
            buffer.Span[0] = LuaValue.Nil;
        }

        return new(1);
    });

    Stream stream;
    StreamWriter? writer;
    StreamReader? reader;
    bool isClosed;

    public bool IsClosed => Volatile.Read(ref isClosed);

    LuaTable? ILuaUserData.Metatable { get => fileHandleMetatable; set => fileHandleMetatable = value; }

    static LuaTable? fileHandleMetatable;

    static FileHandle()
    {
        fileHandleMetatable = new LuaTable();
        fileHandleMetatable[Metamethods.Index] = IndexMetamethod;
    }

    public FileHandle(Stream stream)
    {
        this.stream = stream;
        if (stream.CanRead) reader = new StreamReader(stream);
        if (stream.CanWrite) writer = new StreamWriter(stream);
    }

    public string? ReadLine()
    {
        return reader!.ReadLine();
    }

    public string ReadToEnd()
    {
        return reader!.ReadToEnd();
    }

    public int ReadByte()
    {
        return stream.ReadByte();
    }

    public void Write(ReadOnlySpan<char> buffer)
    {
        writer!.Write(buffer);
    }

    public long Seek(string whence, long offset)
    {
        if (whence != null)
        {
            switch (whence)
            {
                case "set":
                    stream.Seek(offset, SeekOrigin.Begin);
                    break;
                case "cur":
                    stream.Seek(offset, SeekOrigin.Current);
                    break;
                case "end":
                    stream.Seek(offset, SeekOrigin.End);
                    break;
                default:
                    throw new ArgumentException($"Invalid option '{whence}'");
            }
        }

        return stream.Position;
    }

    public void Flush()
    {
        writer!.Flush();
    }

    public void SetVBuf(string mode, int size)
    {
        // Ignore size parameter

        if (writer != null)
        {
            writer.AutoFlush = mode is "no" or "line";
        }
    }

    public void Close()
    {
        if (isClosed) throw new ObjectDisposedException(nameof(FileHandle));
        Volatile.Write(ref isClosed, true);

        if (reader != null)
        {
            reader.Dispose();
        }
        else
        {
            stream.Close();
        }
    }

    static readonly LuaFunction CloseFunction = new("close", (context, buffer, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);

        try
        {
            file.Close();
            buffer.Span[0] = true;
            return new(1);
        }
        catch (IOException ex)
        {
            buffer.Span[0] = LuaValue.Nil;
            buffer.Span[1] = ex.Message;
            buffer.Span[2] = ex.HResult;
            return new(3);
        }
    });

    static readonly LuaFunction FlushFunction = new("flush", (context, buffer, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);

        try
        {
            file.Flush();
            buffer.Span[0] = true;
            return new(1);
        }
        catch (IOException ex)
        {
            buffer.Span[0] = LuaValue.Nil;
            buffer.Span[1] = ex.Message;
            buffer.Span[2] = ex.HResult;
            return new(3);
        }
    });

    static readonly LuaFunction LinesFunction = new("lines", (context, buffer, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);
        var format = context.HasArgument(1)
            ? context.Arguments[1]
            : "*l";


        buffer.Span[0] = new CsClosure("iterator", [new (file),format],static (context, buffer, cancellationToken) =>
        {
            var upValues = context.GetCsClosure()!.UpValues.AsSpan();
            var file = upValues[0].Read<FileHandle>();
            var resultCount = IOHelper.Read(context.State, file, "lines", 0, upValues[1..], buffer, true);
            return new(resultCount);
        });

        return new(1);
    });

    static readonly LuaFunction ReadFunction = new("read", (context, buffer, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);
        var resultCount = IOHelper.Read(context.State, file, "read", 1, context.Arguments[1..], buffer, false);
        return new(resultCount);
    });

    static readonly LuaFunction SeekFunction = new("seek", (context, buffer, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);
        var whence = context.HasArgument(1)
            ? context.GetArgument<string>(1)
            : "cur";
        var offset = context.HasArgument(2)
            ? context.GetArgument<int>(2)
            : 0;

        if (whence is not ("set" or "cur" or "end"))
        {
            throw new LuaRuntimeException(context.State.GetTraceback(), $"bad argument #2 to 'seek' (invalid option '{whence}')");
        }

        try
        {
            buffer.Span[0] = file.Seek(whence, (long)offset);
            return new(1);
        }
        catch (IOException ex)
        {
            buffer.Span[0] = LuaValue.Nil;
            buffer.Span[1] = ex.Message;
            buffer.Span[2] = ex.HResult;
            return new(3);
        }
    });

    static readonly LuaFunction SetVBufFunction = new("setvbuf", (context, buffer, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);
        var mode = context.GetArgument<string>(1);
        var size = context.HasArgument(2)
            ? context.GetArgument<int>(2)
            : -1;

        file.SetVBuf(mode, size);

        buffer.Span[0] = true;
        return new(1);
    });

    static readonly LuaFunction WriteFunction = new("write", (context, buffer, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);
        var resultCount = IOHelper.Write(file, "write", context, buffer);
        return new(resultCount);
    });
}