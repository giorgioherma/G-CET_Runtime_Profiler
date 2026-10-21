namespace GCETRuntimeProfiler.Core.Services;

// PASS8 continues to own frame dormancy and all NPC/audio state.
// Native FAR suppresses only new NPC candidate discovery; no tracked
// entity, pending work or cleanup path is parked.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyF80hArousedNativeWake(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local ANCHORS = dofile(\"AnchorsIndex.lua\")",
            "local function tryRegisterNPC(self)",
            "ObserveAfter(\"NPCPuppet\", \"OnGameAttached\"",
            "registerForEvent(\"onInit\", function()");
        const string marker="-- G-CET native location:f80h-aroused-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("f80h native vicinity stage already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var source=crlf?file.Text.Replace("\r\n","\n",StringComparison.Ordinal):file.Text;
        const string helperAnchor="local function tryRegisterNPC(self)\n";
        const string observeAnchor="  ObserveAfter(\"NPCPuppet\", \"OnGameAttached\", function(self)\n";
        const string initAnchor="registerForEvent(\"onInit\", function()\n";
        if(source.Split(helperAnchor,StringSplitOptions.None).Length!=2 ||
           source.Split(observeAnchor,StringSplitOptions.None).Length!=2 ||
           source.Split(initAnchor,StringSplitOptions.None).Length!=2 ||
           !source.Contains("local tracked      = {}",StringComparison.Ordinal) ||
           !source.Contains("local pending      = {}",StringComparison.Ordinal) ||
           !source.Contains("local pendingAppearance = {}",StringComparison.Ordinal) ||
           !source.Contains("local preActiveQueue",StringComparison.Ordinal))
            throw new InvalidOperationException("f80h attachment, pre-active or pending state changed.");
        var state="""
-- G-CET native location:f80h-aroused-v1
local __gcetF80hWorker = require("GCET_Semantic/f80h_native_wake")
local function __gcetF80hCanSkipNew()
  return __gcetF80hWorker.isFar()
    and next(tracked) == nil and next(pending) == nil
    and next(pendingAppearance) == nil and next(preActiveQueue) == nil
end

""";
        // Helper lives after the original state declarations, immediately
        // before new registration, so each Lua upvalue is already bound.
        source=source.Replace(helperAnchor,state+helperAnchor+
            "  if __gcetF80hCanSkipNew() then return end\n",StringComparison.Ordinal);
        source=source.Replace(observeAnchor,observeAnchor+
            "    if scriptActive and __gcetF80hCanSkipNew() then return end\n",
            StringComparison.Ordinal);
        const string attach="""
  -- 0-Engine remains the single lifecycle/native host authority.
  __gcetF80hWorker.attach(#ANCHORS)
""";
        context.WriteCoordinator("f80h_native_wake.lua", """
-- G-CET semantic coordinator:native-location-f80h-aroused
-- Private native FAR worker; no independent scheduler or gameplay hooks.
local M = {}
local far = false
function M.isFar() return far == true end
function M.attach(count)
  far = false
  if type(count) ~= "number" or count < 1 or count > 1024 then return false end
  local okEngine, engine = pcall(GetMod, "0-Engine")
  local host = okEngine and type(engine) == "table"
    and engine.GCET and engine.GCET.NativeLocationHost or nil
  if not host or type(host.attachCompiled) ~= "function" then return false end
  local ok, attached = pcall(function()
    return host:attachCompiled(10042, count,
      function() far = false end,
      function() far = true end,
      function() far = false; return true end)
  end)
  if not ok or not attached then far = false end
  return ok and attached == true
end
return M
""");
        source=source.Replace(initAnchor,initAnchor+attach+"\n",StringComparison.Ordinal);
        context.Write(file,crlf?source.Replace("\n","\r\n",StringComparison.Ordinal):source);
        return SemanticInjectionResult.Success(
            "Coordinator-owned native wake: installed source anchors compiled to native coarse FAR; only new distant NPC attach/registration is bypassed when all pending/tracked queues are empty. PASS8 frame, NPC lifecycle and audio remain unchanged.");
    }
}
