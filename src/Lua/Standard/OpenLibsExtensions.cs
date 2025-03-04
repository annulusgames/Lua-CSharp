using Lua.Runtime;

namespace Lua.Standard;

public static class OpenLibsExtensions
{
    public static void OpenBasicLibrary(this LuaState state)
    {
        state.Environment["_G"] = state.Environment;
        state.Environment["_VERSION"] = "Lua 5.2";
        foreach (var func in BasicLibrary.Instance.Functions)
        {
            state.Environment[func.Name] = func;
        }
    }

    public static void OpenBitwiseLibrary(this LuaState state)
    {
        var bit32 = new LuaTable(0, BitwiseLibrary.Instance.Functions.Length);
        foreach (var func in BitwiseLibrary.Instance.Functions)
        {
            bit32[func.Name] = func;
        }

        state.Environment["bit32"] = bit32;
        state.LoadedModules["bit32"] = bit32;
    }

    public static void OpenCoroutineLibrary(this LuaState state)
    {
        var coroutine = new LuaTable(0, CoroutineLibrary.Instance.Functions.Length);
        foreach (var func in CoroutineLibrary.Instance.Functions)
        {
            coroutine[func.Name] = func;
        }

        state.Environment["coroutine"] = coroutine;
    }

    public static void OpenIOLibrary(this LuaState state)
    {
        var io = new LuaTable(0, IOLibrary.Instance.Functions.Length);
        foreach (var func in IOLibrary.Instance.Functions)
        {
            io[func.Name] = func;
        }

        io["stdio"] = new LuaValue(new FileHandle(Console.OpenStandardInput()));
        io["stdout"] = new LuaValue(new FileHandle(Console.OpenStandardOutput()));
        io["stderr"] = new LuaValue(new FileHandle(Console.OpenStandardError()));

        state.Environment["io"] = io;
        state.LoadedModules["io"] = io;
    }

    public static void OpenMathLibrary(this LuaState state)
    {
        state.Environment[MathematicsLibrary.RandomInstanceKey] = new(new MathematicsLibrary.RandomUserData(new Random()));

        var math = new LuaTable(0, MathematicsLibrary.Instance.Functions.Length);
        foreach (var func in MathematicsLibrary.Instance.Functions)
        {
            math[func.Name] = func;
        }

        math["pi"] = Math.PI;
        math["huge"] = double.PositiveInfinity;

        state.Environment["math"] = math;
        state.LoadedModules["math"] = math;
    }

    public static void OpenModuleLibrary(this LuaState state)
    {
        var package = new LuaTable();
        package["loaded"] = state.LoadedModules;
        state.Environment["package"] = package;
        state.Environment["require"] = ModuleLibrary.Instance.RequireFunction;
    }

    public static void OpenOperatingSystemLibrary(this LuaState state)
    {
        var os = new LuaTable(0, OperatingSystemLibrary.Instance.Functions.Length);
        foreach (var func in OperatingSystemLibrary.Instance.Functions)
        {
            os[func.Name] = func;
        }

        state.Environment["os"] = os;
        state.LoadedModules["os"] = os;
    }

    public static void OpenStringLibrary(this LuaState state)
    {
        var @string = new LuaTable(0, StringLibrary.Instance.Functions.Length);
        foreach (var func in StringLibrary.Instance.Functions)
        {
            @string[func.Name] = func;
        }

        state.Environment["string"] = @string;
        state.LoadedModules["string"] = @string;

        // set __index
        var key = new LuaValue("");
        if (!state.TryGetMetatable(key, out var metatable))
        {
            metatable = new();
            state.SetMetatable(key, metatable);
        }

        metatable[Metamethods.Index] = new LuaFunction("index", (context, buffer, cancellationToken) =>
        {
            context.GetArgument<string>(0);
            var key = context.GetArgument(1);

            buffer.Span[0] = @string[key];
            return new(1);
        });
    }

    public static void OpenTableLibrary(this LuaState state)
    {
        var table = new LuaTable(0, TableLibrary.Instance.Functions.Length);
        foreach (var func in TableLibrary.Instance.Functions)
        {
            table[func.Name] = func;
        }

        state.Environment["table"] = table;
        state.LoadedModules["table"] = table;
    }
    
    public static void OpenDebugLibrary(this LuaState state)
    {
        var debug = new LuaTable(0, DebugLibrary.Instance.Functions.Length);
        foreach (var func in DebugLibrary.Instance.Functions)
        {
            debug[func.Name] = func;
        }

        state.Environment["debug"] = debug;
        state.LoadedModules["debug"] = debug;
    }

    public static void OpenStandardLibraries(this LuaState state)
    {
        state.OpenBasicLibrary();
        state.OpenBitwiseLibrary();
        state.OpenCoroutineLibrary();
        state.OpenIOLibrary();
        state.OpenMathLibrary();
        state.OpenModuleLibrary();
        state.OpenOperatingSystemLibrary();
        state.OpenStringLibrary();
        state.OpenTableLibrary();
        state.OpenDebugLibrary();
    }
}