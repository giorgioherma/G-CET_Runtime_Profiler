namespace GCETRuntimeProfiler.Core.Services;
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyZiggyNativeWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua","local function __gcetZiggyAttachNative()","local function __gcetZiggyOfferedFar()");
        const string marker="-- G-CET semantic:ziggy-native-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Ziggy native coordinator already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var s=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string old="lookup:attachCompiled(10038,1,function() __gcetZiggyNativeOfferedFar=false end,\n   function() __gcetZiggyNativeOfferedFar=true end,\n   function() __gcetZiggyNativeOfferedFar=nil;return true end)";
        if(s.Split(old,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Ziggy compiled native offered-state callbacks changed.");
        s=s.Replace(old,marker+"\n require(\"GCET_Semantic/ziggy_native_worker\").attach(lookup,\n   function(near) __gcetZiggyNativeOfferedFar=not near end,\n   function() __gcetZiggyNativeOfferedFar=nil;return true end)",StringComparison.Ordinal);
        context.Write(file,crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s);
        context.WriteCoordinator("ziggy_native_worker.lua", """
-- G-CET semantic coordinator:ziggy-native-worker
-- Native owner and scheduling remain centralized in 0-Engine.
local M = {}
function M.attach(host, onKnown, onUnknown)
  if not host or type(host.attachCompiled) ~= "function" then return false end
  local ok, attached = pcall(function()
    return host:attachCompiled(10038, 1,
      function() onKnown(true) end,
      function() onKnown(false) end,
      onUnknown)
  end)
  return ok and attached == true
end
return M
""");
        return SemanticInjectionResult.Success(
            "Ziggy offered-site native subscription moved to coordinator; player identity, quest phase, 130m exact fallback and wake cadence preserved.");
    }
}
