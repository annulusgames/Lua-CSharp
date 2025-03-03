using Lua.Internal;
using Lua.Runtime;
using Lua.Standard.Internal;

namespace Lua.Standard;

public sealed class IOLibrary
{
    public static readonly IOLibrary Instance = new();

    public IOLibrary()
    {
        Functions = [
            new("close", Close),
            new("flush", Flush),
            new("input", Input),
            new("lines", Lines),
            new("open", Open),
            new("output", Output),
            new("read", Read),
            new("type", Type),
            new("write", Write),
        ];
    }

    public readonly LuaFunction[] Functions;

    public ValueTask<int> Close(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var file = context.HasArgument(0)
            ? context.GetArgument<FileHandle>(0)
            : context.State.Environment["io"].Read<LuaTable>()["stdout"].Read<FileHandle>();

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
    }

    public ValueTask<int> Flush(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var file = context.State.Environment["io"].Read<LuaTable>()["stdout"].Read<FileHandle>();

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
    }

    public ValueTask<int> Input(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var io = context.State.Environment["io"].Read<LuaTable>();

        if (context.ArgumentCount == 0 || context.Arguments[0].Type is LuaValueType.Nil)
        {
            buffer.Span[0] = io["stdio"];
            return new(1);
        }

        var arg = context.Arguments[0];
        if (arg.TryRead<FileHandle>(out var file))
        {
            io["stdio"] = new(file);
            buffer.Span[0] = new(file);
            return new(1);
        }
        else
        {
            var stream = File.Open(arg.ToString()!, FileMode.Open, FileAccess.ReadWrite);
            var handle = new FileHandle(stream);
            io["stdio"] = new(handle);
            buffer.Span[0] = new(handle);
            return new(1);
        }
    }

    public ValueTask<int> Lines(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        if (context.ArgumentCount == 0)
        {
            var file = context.State.Environment["io"].Read<LuaTable>()["stdio"].Read<FileHandle>();
            buffer.Span[0] = new CsClosure("iterator",[new (file)] ,static (context, buffer, ct) =>
            {
                var file = context.GetCsClosure()!.UpValues[0].Read<FileHandle>();
                var resultCount = IOHelper.Read(context.State, file, "lines", 0, [], buffer, true);
                if (resultCount > 0 && buffer.Span[0].Type is LuaValueType.Nil)
                {
                    file.Close();
                }
                return new(resultCount);
            });
            return new(1);
        }
        else
        {
            var fileName = context.GetArgument<string>(0);

            using var methodBuffer = new PooledArray<LuaValue>(32);
            IOHelper.Open(context.State, fileName, "r", methodBuffer.AsMemory(), true);

            var file = methodBuffer[0].Read<FileHandle>();
            var upValues = new LuaValue[context.Arguments.Length];
            upValues[0] = new(file);
            context.Arguments[1..].CopyTo(upValues[1..]);

            buffer.Span[0] = new CsClosure("iterator", upValues, static (context, buffer, ct) =>
            {
                var upValues = context.GetCsClosure()!.UpValues;
                var file = upValues[0].Read<FileHandle>();
                var formats = upValues.AsSpan(1);
                var resultCount = IOHelper.Read(context.State, file, "lines", 0, formats, buffer, true);
                if (resultCount > 0 && buffer.Span[0].Type is LuaValueType.Nil)
                {
                    file.Close();
                }
                return new(resultCount);
            });

            return new(1);
        }
    }

    public ValueTask<int> Open(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var fileName = context.GetArgument<string>(0);
        var mode = context.HasArgument(1)
            ? context.GetArgument<string>(1)
            : "r";

        var resultCount = IOHelper.Open(context.State, fileName, mode, buffer, false);
        return new(resultCount);
    }

    public ValueTask<int> Output(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var io = context.State.Environment["io"].Read<LuaTable>();

        if (context.ArgumentCount == 0 || context.Arguments[0].Type is LuaValueType.Nil)
        {
            buffer.Span[0] = io["stdout"];
            return new(1);
        }

        var arg = context.Arguments[0];
        if (arg.TryRead<FileHandle>(out var file))
        {
            io["stdout"] = new(file);
            buffer.Span[0] = new(file);
            return new(1);
        }
        else
        {
            var stream = File.Open(arg.ToString()!, FileMode.Open, FileAccess.ReadWrite);
            var handle = new FileHandle(stream);
            io["stdout"] = new(handle);
            buffer.Span[0] = new(handle);
            return new(1);
        }
    }

    public ValueTask<int> Read(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var file = context.State.Environment["io"].Read<LuaTable>()["stdio"].Read<FileHandle>();
        var resultCount = IOHelper.Read(context.State, file, "read", 0, context.Arguments, buffer, false);
        return new(resultCount);
    }

    public ValueTask<int> Type(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (arg0.TryRead<FileHandle>(out var file))
        {
            buffer.Span[0] = file.IsClosed ? "closed file" : "file";
        }
        else
        {
            buffer.Span[0] = LuaValue.Nil;
        }

        return new(1);
    }

    public ValueTask<int> Write(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken)
    {
        var file = context.State.Environment["io"].Read<LuaTable>()["stdout"].Read<FileHandle>();
        var resultCount = IOHelper.Write(file, "write", context, buffer);
        return new(resultCount);
    }
}