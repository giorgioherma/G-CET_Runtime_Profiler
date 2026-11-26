-- Execute the actual generated pose path; compare every native read and
-- downstream invocation while injecting failures and changing receivers.
local root=assert(arg[1])
local originalPcall=pcall
local function run(path,backend)
    local trace,identities={},{}
    local current,fail
    local calls=0
    Game={}
    if backend and backend~="missing" then
        local function typed(player)
            calls=calls+1
            assert(player==current,"receiver changed")
            -- The native typed ABI cannot represent a nil Vector4; refuse
            -- this synthetic source-shaped case before any engine read.
            if backend=="unsupported" or fail==2 then return 0 end
            local ok,p,o,yaw=originalPcall(function()
                local position=player:GetWorldPosition()
                local orientation=player:GetWorldOrientation()
                return position,orientation,orientation:ToEulerAngles().yaw
            end)
            if not ok then
                if backend=="throws" then error("native invocation failed") end
                return 2
            end
            return 1,p,o,yaw
        end
        Game.GCETReadPlayerPoseV1=typed
        Game.GCETReadSharedPlayerPoseV1=typed
    end
    State={}
    function CurrentPlayer()return current end
    function downstream()
        trace[#trace+1]="downstream:"..tostring(State.pos and State.pos.x)..":"..tostring(State.yaw)
    end
    function pcall(fn,...)
        identities[fn]=true
        return originalPcall(fn,...)
    end
    local update=assert(dofile(path))
    for i=1,32 do
        fail=i%8
        local id=i
        current={
            GetWorldPosition=function(self)
                assert(self==current)
                trace[#trace+1]="pos:"..id
                if fail==1 then error("position failure") end
                if fail==2 then return nil end
                return {x=id}
            end,
            GetWorldOrientation=function(self)
                assert(self==current)
                trace[#trace+1]="orientation:"..id
                if fail==3 then error("orientation failure") end
                if fail==4 then return nil end
                return {ToEulerAngles=function()
                    trace[#trace+1]="euler:"..id
                    if fail==5 then error("Euler failure") end
                    return {yaw=id*2}
                end}
            end
        }
        if fail==6 then current=nil end
        update(0)
        trace[#trace+1]="state:"..tostring(State.pos and State.pos.x)..":"..tostring(State.yaw)
    end
    pcall=originalPcall
    local count=0
    for _ in pairs(identities) do count=count+1 end
    return table.concat(trace,"|"),count,calls
end
local a,ac=run(root.."/pose-original.lua")
local b,bc=run(root.."/pose-generated.lua")
assert(a==b,"pose native reads/failure/state trace differs")
assert(ac==28 and bc==1,"expected 28 original closures vs one reused function: "..ac.."/"..bc)
print("Pose: exact lookup/failure traces, 28 protected closures -> 1 reusable function PASS")

for _,backend in ipairs({"complete","unsupported","missing","throws"}) do
    local trace,_,calls=run(root.."/pose-typed.lua",backend)
    assert(trace==a,"typed pose original read/failure/publication trace differs: "..backend)
    assert(calls==(backend=="missing" and 0 or 28),"unexpected native backend attempt count")
end
print("Typed pose source: original receiver/read/order/state trace, unsupported-only fallback, missing ABI, native failure and throw without replay PASS")

for _,backend in ipairs({"complete","unsupported","missing","throws"}) do
    local trace,_,calls=run(root.."/pose-shared.lua",backend)
    assert(trace==a,"shared pose exact read/failure trace differs: "..backend)
    assert(calls==(backend=="missing" and 0 or 28),"unexpected shared backend attempt count")
end
-- Actual generated host registration, two independent owners. Explicit
-- coarse arming only while supported; disarm remains original exact ABI.
for _,supported in ipairs({true,false}) do
    local coarse,exact,disarm,scheduled=0,0,0,0
    Game={
        GCETStaticReady=function()return true end,
        GCETStaticOwnerCount=function(id)return 1 end,
        GCETStaticOwnerAnchor=function(id,n)return id*10+n end,
        GCETStaticArm=function(id,armed)
            if armed then exact=exact+1 else disarm=disarm+1 end
            return true
        end
    }
    if supported then Game.GCETStaticArmCoarseSpatialV1=function(id,armed)
        assert(armed);coarse=coarse+1;return true
    end end
    local host=assert(dofile(root.."/spatial-shared.lua")).new({
        Register=function()return {Schedule={Every=function()
            scheduled=scheduled+1;return {Cancel=function()end}
        end}}end
    })
    local cb=function()return true end
    assert(host:attachCompiled(1,1,cb,cb,cb))
    assert(host:attachCompiled(2,1,cb,cb,cb))
    assert(scheduled==1 and coarse==(supported and 2 or 0) and exact==(supported and 0 or 2))
    host:shutdown()
    assert(disarm==2)
end
print("Shared exact pose + explicit two-owner coarse spatial registration, older ABI and cleanup PASS")
