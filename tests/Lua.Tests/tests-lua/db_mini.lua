-- testing debug library



local a = 1

local function multi_assert(expected, ...)
    local arg = { ... }
    for i = 1, #arg do
        assert(arg[i] == expected[i])
    end
end
local function test_locals(x, ...)
    local b = "local b"
    assert(debug.getlocal(test_locals, 1) == "x")
    multi_assert({ "x", 1 }, debug.getlocal(1, 1))
    multi_assert({ "b", "local b" }, debug.getlocal(1, 2))
    multi_assert({ "(vararg)", 2 }, debug.getlocal(1, -1))
    multi_assert({ "(vararg)", 3 }, debug.getlocal(1, -2))
    multi_assert({ "a", 1 }, debug.getlocal(2, 1))
    assert(debug.setlocal(2, 1, "new a") == "a")

end

test_locals(1, 2, 3)
assert(a == "new a")

-- test file and string names truncation
a = "function f () end"
local function dostring (s, x)
    return load(s, x)()
end
dostring(a)
assert(debug.getinfo(f).short_src == string.format('[string "%s"]', a))
dostring(a .. string.format("; %s\n=1", string.rep('p', 400)))
assert(string.find(debug.getinfo(f).short_src, '^%[string [^\n]*%.%.%."%]$'))
dostring(a .. string.format("; %s=1", string.rep('p', 400)))
assert(string.find(debug.getinfo(f).short_src, '^%[string [^\n]*%.%.%."%]$'))
dostring("\n" .. a)
assert(debug.getinfo(f).short_src == '[string "..."]')
dostring(a, "")
assert(debug.getinfo(f).short_src == '[string ""]')
dostring(a, "@xuxu")
assert(debug.getinfo(f).short_src == "xuxu")
dostring(a, "@" .. string.rep('p', 1000) .. 't')
assert(string.find(debug.getinfo(f).short_src, "^%.%.%.p*t$"))
dostring(a, "=xuxu")
assert(debug.getinfo(f).short_src == "xuxu")
dostring(a, string.format("=%s", string.rep('x', 500)))
assert(string.find(debug.getinfo(f).short_src, "^x*$"))
dostring(a, "=")
assert(debug.getinfo(f).short_src == "")
a = nil;
f = nil;

repeat
    local g = { x = function()
        local a = debug.getinfo(2)
        assert(a.name == 'f' and a.namewhat == 'local')
        a = debug.getinfo(1)
        assert(a.name == 'x' and a.namewhat == 'field')
        return 'xixi'
    end }
    local f = function()
        return 1 + 1 and (not 1 or g.x())
    end
    assert(f() == 'xixi')
    g = debug.getinfo(f)
    assert(g.what == "Lua" and g.func == f and g.namewhat == "" and not g.name)

    function f (x, name)
        -- local!
        if not name then
            name = 'f'
        end
        local a = debug.getinfo(1)
        print(a.name, a.namewhat, name)
        assert(a.name == name and a.namewhat == 'local')
        return x
    end

    -- breaks in different conditions
    if 3 > 4 then
        break
    end ;
    f()
    if 3 < 4 then
        a = 1
    else
        break
    end ;
    f()
    while 1 do
        local x = 10;
        break
    end ;
    f()
    local b = 1
    if 3 > 4 then
        return math.sin(1)
    end ;
    f()
    a = 3 < 4;
    f()
    a = 3 < 4 or 1;
    f()
    repeat local x = 20;
        if 4 > 3 then
            f()
        else
            break
        end ;
        f() until 1
    g = {}
    f(g).x = f(2) and f(10) + f(9)
    assert(g.x == f(19))
    function g(x)
        if not x then
            return 3
        end
        return (x('a', 'x'))
    end
    assert(g(f) == 'a')
until 1

local function test_upvalues()
    local a = 3
    local function f(x)
        local b = a + x
        local function g(y)
            local c = b + y
            local function h()
                return a + b + c
            end
            multi_assert({ "a", 3 }, debug.getupvalue(h, 1))
            multi_assert({ "b", 4 }, debug.getupvalue(h, 2))
            multi_assert({ "c", 6 }, debug.getupvalue(h, 3))
            multi_assert({ "b", 4 }, debug.getupvalue(g, 1))
            multi_assert({ "a", 3 }, debug.getupvalue(g, 2))
            multi_assert({ "a", 3 }, debug.getupvalue(f, 1))
            debug.setupvalue(h, 1, 10)
            debug.setupvalue(h, 2, 20)
            debug.setupvalue(h, 3, 30)
            assert(h() == 60)
        end
        g(2)
    end
    f(1)
end
test_upvalues()
local mt = {
    __metatable = "my own metatable",
    __index = function(o, k)
        return o + k
    end
}

local a = 1
local b = 2
local function f()
    return a
end
local function g()
    return b
end

debug.upvaluejoin(f, 1, g, 1)

assert(f() == 2)
b = 3
assert(f() == 3)

debug.setmetatable(10, mt)
assert(debug.getmetatable(10) == mt)
a = 10
assert(a[3] == 13)

assert(debug.traceback(print) == print)
assert(debug.traceback(print) == print)

assert(type(debug.getregistry()) == "table")

-- testing nparams, nups e isvararg
local t = debug.getinfo(print, "u")
assert(t.isvararg == true and t.nparams == 0 and t.nups == 0)

t = debug.getinfo(function(a, b, c)
end, "u")
assert(t.isvararg == false and t.nparams == 3 and t.nups == 0)

t = debug.getinfo(function(a, b, ...)
    return t[a]
end, "u")
assert(t.isvararg == true and t.nparams == 2 and t.nups == 1)

t = debug.getinfo(1)   -- main
assert(t.isvararg == true and t.nparams == 0 and t.nups == 1 and
        debug.getupvalue(t.func, 1) == "_ENV")


-- testing debugging of coroutines

local function checktraceback (co, p, level)
    local tb = debug.traceback(co, nil, level)
    local i = 0
    for l in string.gmatch(tb, "[^\n]+\n?") do
        assert(i == 0 or string.find(l, p[i]))
        i = i + 1
    end
    assert(p[i] == nil)
end

local function f (n)
    if n > 0 then
        f(n - 1)
    else
        coroutine.yield()
    end
end

local co = coroutine.create(f)
coroutine.resume(co, 3)
checktraceback(co, { "yield", "db_mini.lua", "db_mini.lua", "db_mini.lua", "db_mini.lua" })
checktraceback(co, { "db_mini.lua", "db_mini.lua", "db_mini.lua", "db_mini.lua" }, 1)
checktraceback(co, { "db_mini.lua", "db_mini.lua", "db_mini.lua" }, 2)
checktraceback(co, { "db_mini.lua" }, 4)
checktraceback(co, {}, 40)