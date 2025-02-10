using Lua.Standard;

namespace Lua.Tests;

[LuaObject]
public partial class TestUserData
{
    [LuaMember]
    public double Property { get; set; }

    [LuaMember("p2")]
    public string PropertyWithName { get; set; } = "";

    [LuaMember]
    public static void MethodVoid()
    {
        Console.WriteLine("HEY!");
    }

    [LuaMember]
    public static async Task MethodAsync()
    {
        await Task.CompletedTask;
    }

    [LuaMember]
    public static double StaticMethodWithReturnValue(double a, double b)
    {
        return a + b;
    }

    [LuaMember]
    public double InstanceMethodWithReturnValue()
    {
        return Property;
    }

    [LuaMetamethod(LuaObjectMetamethod.Call)]
    public string Call()
    {
        return "Called!";
    }
}

public class LuaObjectTests
{
    [Test]
    public async Task Test_Property()
    {
        var userData = new TestUserData()
        {
            Property = 1
        };

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test.Property");

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new LuaValue(1)));
    }

    [Test]
    public async Task Test_PropertyWithName()
    {
        var userData = new TestUserData()
        {
            PropertyWithName = "foo",
        };

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test.p2");

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new LuaValue("foo")));
    }

    [Test]
    public async Task Test_MethodVoid()
    {
        var userData = new TestUserData();

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test.MethodVoid()");

        Assert.That(results, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task Test_MethodAsync()
    {
        var userData = new TestUserData();

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test.MethodAsync()");

        Assert.That(results, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task Test_StaticMethodWithReturnValue()
    {
        var userData = new TestUserData();

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test.StaticMethodWithReturnValue(1, 2)");

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new LuaValue(3)));
    }

    [Test]
    public async Task Test_InstanceMethodWithReturnValue()
    {
        var userData = new TestUserData()
        {
            Property = 1
        };

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test:InstanceMethodWithReturnValue()");

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new LuaValue(1)));
    }

    [Test]
    public async Task Test_CallMetamethod()
    {
        var userData = new TestUserData();

        var state = LuaState.Create();
        state.OpenBasicLibrary();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("""
                                                assert(test() == 'Called!')
                                                return test()
                                                """);

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new LuaValue("Called!")));
    }
}