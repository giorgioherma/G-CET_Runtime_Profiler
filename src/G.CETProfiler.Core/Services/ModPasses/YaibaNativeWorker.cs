namespace GCETRuntimeProfiler.Core.Services;
internal static partial class SemanticInjectors
{
    internal static SemanticInjectionResult ApplyYaibaNativeWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("bathtub.lua","function bathtub.init()","function bathtub.update(player, pPos)","__gcetBathNativeFar");
        const string marker="-- G-CET semantic:yaiba-native-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Yaiba native worker already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var s=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string old="lookup:attachCompiled(10037,1,function() __gcetBathNativeFar=false end,\n            function() __gcetBathNativeFar=true end,\n            function() __gcetBathNativeFar=nil;return true end)";
        if(s.Split(old,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Yaiba bathtub native owner and fallback source changed.");
        s=s.Replace(old,marker+"\n        require(\"GCET_Semantic/yaiba_native_worker\").attach(lookup,\n            function(near) __gcetBathNativeFar=not near end,\n            function() __gcetBathNativeFar=nil;return true end)",
            StringComparison.Ordinal);
        context.Write(file,crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s);
        context.WriteCoordinator("yaiba_native_worker.lua", """
-- G-CET semantic coordinator:yaiba-native-worker
-- Native subscription only; bathtub UI, hub, pin and water remain source-owned.
local M = {}
function M.attach(host, onKnown, onUnknown)
  if not host or type(host.attachCompiled) ~= "function" then return false end
  local ok, attached = pcall(function()
    return host:attachCompiled(10037, 1,
      function() onKnown(true) end,
      function() onKnown(false) end,
      onUnknown)
  end)
  return ok and attached == true
end
return M
""".Replace("-- G-CET semantic coordinator:yaiba-native-worker",
        "-- G-CET semantic coordinator:" + context.RuleId, StringComparison.Ordinal));
        return SemanticInjectionResult.Success(
            "Yaiba native bathtub proximity moved to worker; active UI/water and original UNKNOWN geometry remain.");
    }
}
