namespace GCETRuntimeProfiler.Core.Services;
internal static partial class SemanticInjectors
{
    internal static SemanticInjectionResult ApplyYaibaNativeWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("bathtub.lua","function bathtub.init()","function bathtub.update(player, pPos)","__gcetBathNativeFar");
        const string marker="-- G-CET semantic:yaiba-native-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
        {
            if (!NativeLocationAdditionalOwnersSource.BathtubBridgeComplete(file.Text))
                throw new InvalidOperationException(
                    "Yaiba native source bridge is incomplete.");
            var worker=context.TryReadOwnedCoordinator("yaiba_native_worker.lua");
            if (worker is not null)
            {
                if (!YaibaWorkerComplete(worker,context.RuleId))
                    throw new InvalidOperationException(
                        "Yaiba native worker source drift.");
                return SemanticInjectionResult.Skip(
                    "Source-proven Yaiba native worker already complete.");
            }
            // A partial deployment can retain the bridge but lose the worker.
            // Restore ONLY that source-owned dependency; no duplicate callback.
            WriteYaibaWorker(context);
            return SemanticInjectionResult.Success(
                "Restored missing Yaiba native coordinator.");
        }
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var s=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string old="lookup:attachCompiled(10037,1,function() __gcetBathNativeFar=false end,\n            function() __gcetBathNativeFar=true end,\n            function() __gcetBathNativeFar=nil;return true end)";
        if(s.Split(old,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Yaiba bathtub native owner and fallback source changed.");
        s=s.Replace(old,marker+"\n        require(\"GCET_Semantic/yaiba_native_worker\").attach(lookup,\n            function(near) __gcetBathNativeFar=not near end,\n            function() __gcetBathNativeFar=nil;return true end)",
            StringComparison.Ordinal);
        context.Write(file,crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s);
        WriteYaibaWorker(context);
        return SemanticInjectionResult.Success(
            "Yaiba native bathtub proximity moved to worker; active UI/water and original UNKNOWN geometry remain.");
    }
    private static bool YaibaWorkerComplete(string worker, string rule)
    {
        var source=worker.Replace("\r\n","\n",StringComparison.Ordinal);
        return source.StartsWith("-- G-CET semantic coordinator:"+rule+"\n",StringComparison.Ordinal) &&
            source.Contains("function M.attach(host, onKnown, onUnknown)",StringComparison.Ordinal) &&
            source.Contains("host:attachCompiled(10037, 1,",StringComparison.Ordinal) &&
            source.Contains("function() onKnown(true) end",StringComparison.Ordinal) &&
            source.Contains("function() onKnown(false) end",StringComparison.Ordinal) &&
            source.Contains("onUnknown)",StringComparison.Ordinal) &&
            source.Contains("return ok and attached == true",StringComparison.Ordinal) &&
            source.TrimEnd().EndsWith("return M",StringComparison.Ordinal);
    }

    private static void WriteYaibaWorker(SemanticPatchContext context)
    {
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
    }
}
