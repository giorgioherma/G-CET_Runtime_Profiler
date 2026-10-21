namespace GCETRuntimeProfiler.Core.Services;
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyDedkaNativeWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua","dedka_entrance1","dedka_entrance2_cluster","attachCompiled(10034,2");
        const string marker="-- G-CET semantic:dedka-native-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Dedka native coordinator already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var s=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string old="lookup:attachCompiled(10034,2,";
        if(s.Split(old,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Dedka compiled two-zone owner must be unique.");
        var start=s.IndexOf(old,StringComparison.Ordinal);
        var span=s[start..Math.Min(s.Length,start+1900)];
        bool structural=span.Contains("__gcetDedkaRetire()",StringComparison.Ordinal)
            && span.Contains("__gcetDedkaRestore()",StringComparison.Ordinal);
        bool legacy=span.Contains("__gcetRetireZones()",StringComparison.Ordinal)
            && span.Contains("__gcetRestoreZones()",StringComparison.Ordinal);
        if(!structural&&!legacy)
            throw new InvalidOperationException("Dedka zone retire/restore source lifecycle changed.");
        if(!span.Contains("startShopDetail()",StringComparison.Ordinal) ||
           !span.Contains("stopShopDetail()",StringComparison.Ordinal))
            throw new InvalidOperationException("Dedka native shop callbacks changed.");
        s=s.Replace(old,marker+"\n              require(\"GCET_Semantic/dedka_native_worker\").attach(lookup,",
            StringComparison.Ordinal);
        context.Write(file,crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s);
        context.WriteCoordinator("dedka_native_worker.lua", """
-- G-CET semantic coordinator:dedka-native-worker
-- Native owner 10034 only. Source retains author zones and shop lifecycle.
local M = {}
function M.attach(host, onNear, onFar, onUnknown)
  if not host or type(host.attachCompiled) ~= "function" then return false end
  local ok, attached = pcall(function()
    return host:attachCompiled(10034, 2, onNear, onFar, onUnknown)
  end)
  return ok and attached == true
end
return M
""");
        return SemanticInjectionResult.Success(
            "Dedka native two-zone registration delegated to generated Lua worker; zone retirement, restoration and shop detail callbacks retained.");
    }
}
