local root=arg[1]
local function exercise(module,host)
    local gets,reads,trace=0,0,{}
    TestPlayer={}
    local unavailable=false
    local function system(name,method,fn)
        return function()
            gets=gets+1
            if unavailable then return nil end
            return {[method]=function(_,arg)
                reads=reads+1
                trace[#trace+1]=name..":"..tostring(arg==TestPlayer)
                return fn()
            end}
        end
    end
    local tick=0
    Game={
        GetCameraSystem=system("cam","GetActiveCameraForward",function()return tick end),
        GetTransactionSystem=system("txn","GetItemInSlot",function()return tick%2==0 end),
        GetWorkspotSystem=system("work","IsActorInWorkspot",function()return tick%3==0 end),
        GetTimeSystem=system("time","GetGameTime",function()return tick*10 end),
        GetScriptableSystemsContainer=system("container","Get",function()return tick+9 end)
    }
    package.loaded["modules/DerivedState"]=dofile(root.."/shared-"..module.."-module.lua")
    local E=dofile(root.."/shared-"..host.."-host.lua")
    E.Init({})
    for i=1,120 do
        tick=i
        if i==61 then TestPlayer={} end
        -- Two external consumers use the same existing root providers.
        E.GetCameraSystem();E.GetCameraSystem()
        local a,b,c,d,e=E.Update(TestPlayer)
        assert(a==i and b==(i%2==0) and c==(i%3==0) and d==i*10 and e==i+9,
            "mutable state must be read at original invocation")
    end
    local before=gets
    TestPlayer={};unavailable=true
    assert(not pcall(E.Update,TestPlayer),"nil provider must preserve original failure")
    assert(gets==before+1,"nil must not replay getter")
    unavailable=false
    E.GetCameraSystem()
    assert(gets==before+2,"nil never cached")
    return before,reads,table.concat(trace,";")
end
local a,b,c=exercise("stock","stock")
local d,e,f=exercise("generated","generated")
assert(a==602 and d==10 and b==e and b==600 and c==f,"shared root producer lookup counts or original state reads differ")
local g,h,i=exercise("generated","stock")
local j,k,l=exercise("stock","generated")
assert(g==a and j==a and h==b and k==b and i==c and l==c,"mixed overlay fallback changed behavior")
print("Shared derived stable system native lookups: "..a.." -> "..d.."; mutable state reads "..b.." -> "..e.."; mixed overlays, nil retry, epoch change PASS")
