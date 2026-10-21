namespace GCETRuntimeProfiler.Core.Services;
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyCyberStuntsNativeWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua","local __gcetStuntsFixedCount=#jumps","local function __gcetStuntsAttachNative()","__gcetStuntsLocationSleeping()");
        const string marker="-- G-CET semantic:cyberstunts-native-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("CyberStunts native worker already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var s=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string original="host:attachCompiled(10039,__gcetStuntsFixedCount,\n            function() __gcetStuntsNativeFar=false;wasVehicleAirborne=nil end,\n            function() __gcetStuntsNativeFar=true;wasVehicleAirborne=nil end,\n            function() __gcetStuntsNativeFar=nil;wasVehicleAirborne=nil;return true end)";
        if(s.Split(original,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("CyberStunts native fixed-jump callback structure changed.");
        s=s.Replace(original,marker+"\n        require(\"GCET_Semantic/cyberstunts_native_worker\").attach(host,__gcetStuntsFixedCount,\n            function(near) __gcetStuntsNativeFar=not near;wasVehicleAirborne=nil end,\n            function() __gcetStuntsNativeFar=nil;wasVehicleAirborne=nil;return true end)",
            StringComparison.Ordinal);
        context.Write(file,crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s);
        context.WriteCoordinator("cyberstunts_native_worker.lua", """
-- G-CET semantic coordinator:cyberstunts-native-worker
-- Native owner 10039; original stunt detection and jump edits remain local.
local M = {}
function M.attach(host, count, onKnown, onUnknown)
  if not host or type(host.attachCompiled) ~= "function"
      or type(count) ~= "number" or count < 1 then return false end
  local ok, attached = pcall(function()
    return host:attachCompiled(10039, count,
      function() onKnown(true) end,
      function() onKnown(false) end,
      onUnknown)
  end)
  return ok and attached == true
end
return M
""");
        return SemanticInjectionResult.Success(
            "CyberStunts compiled fixed-jump native attach moved to Lua worker; custom jumps, vehicle airborne reset, scoring and UNKNOWN remain original.");
    }
}
