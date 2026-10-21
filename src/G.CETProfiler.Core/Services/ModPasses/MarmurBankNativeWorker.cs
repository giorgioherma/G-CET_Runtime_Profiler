using System.Text.RegularExpressions;
namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyMarmurBankNativeWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("module/Bank.lua",
            "function bank:registerSpatialSet()", "function bank:distanceListener()",
            "self.__gcetNativeAttached");
        const string marker="-- G-CET semantic:marmurbank-native-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Marmur Bank native coordinator already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var s=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        var pattern=new Regex(
            @"self\.__gcetNativeAttached\s*=\s*lookup:attachCompiled\(\s*10036\s*,\s*#PosData\.All\s*,(?<callbacks>.*?)end\)\s*==\s*true",
            RegexOptions.Singleline|RegexOptions.CultureInvariant);
        var matches=pattern.Matches(s);
        if(matches.Count!=1)
            throw new InvalidOperationException("Marmur source must have one compiled native subscription.");
        var callbacks=matches[0].Groups["callbacks"].Value;
        foreach(var proof in new[]{
            "self.__gcetNativeFar=false",
            "self.__gcetNativeFar=true",
            "self:registerSpatialSet()",
            "self.nearestLocation=nil",
            "self.spatialHandle:unregister()",
            "self.spatialHandle=nil",
            "return true"
        })
            if(!callbacks.Contains(proof,StringComparison.Ordinal))
                throw new InvalidOperationException("Marmur native callback lifecycle changed: "+proof);
        // Prevent a misleading match when an update adds a second registration
        // whose owner/identity differs.
        if(s.Split("lookup:attachCompiled(",StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Marmur native host ownership is ambiguous.");
        var old=matches[0];
        s=s[..old.Index]+marker+"\n"+
            "self.__gcetNativeAttached = require(\"GCET_Semantic/marmurbank_native_worker\").attach(self, lookup, #PosData.All)"
            +s[(old.Index+old.Length)..];
        context.Write(file,crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s);
        context.WriteCoordinator("marmurbank_native_worker.lua", """
-- G-CET semantic coordinator:marmurbank-native-worker
-- 0-Engine remains the native location owner and scheduler.
-- Bank account/UI state and exact distanceListener stay in the author mod.
local M = {}
function M.attach(bank, host, count)
  if type(host) ~= "table" or type(host.attachCompiled) ~= "function"
      or type(count) ~= "number" or count < 1 then return false end
  local ok, attached = pcall(function()
    return host:attachCompiled(10036, count,
      function()
        bank.__gcetNativeFar = false
        bank:registerSpatialSet()
      end,
      function()
        bank.__gcetNativeFar = true
        bank.nearestLocation = nil
        if bank.spatialHandle then
          bank.spatialHandle:unregister()
          bank.spatialHandle = nil
        end
      end,
      function()
        bank.__gcetNativeFar = false
        bank:registerSpatialSet()
        return true
      end)
  end)
  if not ok or not attached then bank.__gcetNativeFar = false end
  return ok and attached == true
end
return M
""");
        return SemanticInjectionResult.Success(
            "Moved Marmur native banker wake/park subscriptions to a generated 0-Engine worker; kept bank UI, exact nearest and spatial lifecycle unchanged.");
    }
}
