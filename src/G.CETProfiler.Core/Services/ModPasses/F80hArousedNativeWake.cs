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
local __gcetF80hFar = nil
local function __gcetF80hCanSkipNew()
  return __gcetF80hFar == true
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
  -- Native FAR is authoritative only after initial snapshot. UNKNOWN or
  -- missing host keeps all original CET NPC attachment work alive.
  local okEngine, engine = pcall(GetMod, "0-Engine")
  local host = okEngine and type(engine) == "table"
    and engine.GCET and engine.GCET.NativeLocationHost or nil
  if host and type(host.attachCompiled) == "function" and #ANCHORS == 49 then
    local ok, attached = pcall(function()
      return host:attachCompiled(10042, #ANCHORS,
        function() __gcetF80hFar = false end,
        function() __gcetF80hFar = true end,
        function() __gcetF80hFar = nil; return true end)
    end)
    if not ok or not attached then __gcetF80hFar = nil end
  end
""";
        source=source.Replace(initAnchor,initAnchor+attach+"\n",StringComparison.Ordinal);
        context.Write(file,crlf?source.Replace("\n","\r\n",StringComparison.Ordinal):source);
        return SemanticInjectionResult.Success(
            "49 installed source anchors compiled to native coarse FAR; only new distant NPC attach/registration is bypassed when all pending/tracked queues are empty. PASS8 frame, NPC lifecycle and audio remain unchanged.");
    }
}
