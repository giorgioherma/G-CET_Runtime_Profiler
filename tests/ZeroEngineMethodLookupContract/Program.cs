using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using GCETRuntimeProfiler.Core.Services;

var core=typeof(PassGeneratorService).Assembly;
var resolver=core.GetType("GCETRuntimeProfiler.Core.Services.ZeroEngineMethodResolutionSource",true)!;
var method=resolver.GetMethod("Repair",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)!;
string Repair(string input,out int changes)
{
    object?[] args=[input,0];
    var result=(string)method.Invoke(null,args)!;
    changes=(int)args[1]!;
    return result;
}
void Assert(bool value,string text)
{
    if(!value)throw new InvalidOperationException(text);
}
const string prefix="""
-- G-CET shared provider: PLAYER
local __gcetSharedPlayerProvider = nil
local function __gcetGetPlayer()
    if __gcetSharedPlayerProvider == nil then
        local __gcetOk, __gcetEngine = pcall(GetMod, "0-Engine")
        if __gcetOk and type(__gcetEngine) == "table" then
            local __gcetApi = type(__gcetEngine.GCET) == "table" and __gcetEngine.GCET or __gcetEngine
            if type(__gcetApi.GetPlayer) == "function" then __gcetSharedPlayerProvider = __gcetApi.GetPlayer end
        end
    end
end
""";
var changed=Repair(prefix,out var n);
Assert(n==1 && changed.Contains("type(__gcetEngine.GCET.GetPlayer) == \"function\""),
    "Legacy player wrapper still chooses a namespace instead of its method");
Assert(Repair(changed,out n)==changed && n==0,"Player repair is not idempotent");
const string frame="""
-- G-CET generated frame registrar: source-selected from resolver evidence.
local __gcetRegisterEvent_9 = registerForEvent
do
    local __gcetOk, __gcetEngine = pcall(GetMod, "0-Engine")
    local __gcetApi = __gcetEngine
    if __gcetOk and type(__gcetEngine) == "table" and type(__gcetEngine.GCET) == "table" then __gcetApi = __gcetEngine.GCET end
    if __gcetOk and type(__gcetApi) == "table" and type(__gcetApi.MakeEventRegistrar) == "function" then
        __gcetRegisterEvent_9 = __gcetApi.MakeEventRegistrar("Example", registerForEvent)
    end
end
""";
changed=Repair(frame,out n);
Assert(n==1 && changed.Contains("type(__gcetEngine.GCET.MakeEventRegistrar) == \"function\""),
    "Frame router did not retain top-level registrar fallback");
const string actions="""
local __gcetRouted_7 = false
local __gcetHandles_7 = {}
local __gcetOk_7, __gcetEngine_7 = pcall(GetMod, "0-Engine")
local __gcetApi_7 = __gcetEngine_7
if __gcetOk_7 and type(__gcetEngine_7) == "table" and type(__gcetEngine_7.GCET) == "table" then __gcetApi_7 = __gcetEngine_7.GCET end
if __gcetOk_7 and type(__gcetApi_7) == "table" and type(__gcetApi_7.SubscribeAction) == "function" then
    __gcetRouted_7 = pcall(function() __gcetApi_7.SubscribeAction({actions={"Dodge"}}, handler) end)
end
""";
changed=Repair(actions,out n);
Assert(n==1 && changed.Contains("type(__gcetEngine_7.GCET.SubscribeAction) == \"function\""),
    "Action router did not retain top-level SubscribeAction");
Assert(Repair(changed,out n)==changed && n==0,"Action repair is not idempotent");
Assert(Repair("local __gcetApi = type(__gcetEngine.GCET) == \"table\" and __gcetEngine.GCET or __gcetEngine",out n)
    .Contains("and __gcetEngine.GCET or __gcetEngine") && n==0,
    "Author file without a G-CET marker was modified");
Console.WriteLine("GCET method-level API lookup and legacy generated wrappers PASS");