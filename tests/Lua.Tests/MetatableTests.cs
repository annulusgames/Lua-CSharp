using Lua.Standard;

namespace Lua.Tests;

public class MetatableTests
{
    LuaState state = default!;

    [OneTimeSetUp]
    public void SetUp()
    {
        state = LuaState.Create();
        state.OpenBasicLibrary();
    }

    [Test]
    public async Task Test_Metamethod_Add()
    {
        var source = @"
metatable = {
    __add = function(a, b)
        local t = { }
        for i = 1, #a do
            t[i] = a[i] + b[i]
        end
        return t
    end
}

local a = { 1, 2, 3 }
local b = { 4, 5, 6 }

setmetatable(a, metatable)

return a + b
";

        var result = await state.DoStringAsync(source);
        Assert.That(result, Has.Length.EqualTo(1));

        var table = result[0].Read<LuaTable>();
        Assert.Multiple(() =>
        {
            Assert.That(table[1].Read<double>(), Is.EqualTo(5));
            Assert.That(table[2].Read<double>(), Is.EqualTo(7));
            Assert.That(table[3].Read<double>(), Is.EqualTo(9));
        });
    }

    [Test]
    public async Task Test_Metamethod_Index()
    {
        var source = @"
metatable = {
    __index = {x=1}
}

local a = {}
setmetatable(a, metatable)
assert(a.x == 1)
metatable.__index= nil
assert(a.x == nil)
metatable.__index= function(a,b) return b end
assert(a.x == 'x')
";
        await state.DoStringAsync(source);
    }

    [Test]
    public async Task Test_Metamethod_NewIndex()
    {
        var source = @"
metatable = {
    __newindex = {}
}

local a = {}
a.x = 1
setmetatable(a, metatable)
a.x = 2
assert(a.x == 2)
a.x = nil
a.x = 2
assert(a.x == nil)
assert(metatable.__newindex.x == 2)
";
        await state.DoStringAsync(source);
    }

    [Test]
    public async Task Test_Metamethod_Call()
    {
        var source = @"
metatable = {
    __call = function(a, b)
        return a.x + b
    end
}

local a = {}
a.x = 1
setmetatable(a, metatable)
assert(a(2) == 3)
function tail(a, b)
    return a(b)
end
tail(a, 3)
assert(tail(a, 3) == 4)
";
        await state.DoStringAsync(source);
    }
}