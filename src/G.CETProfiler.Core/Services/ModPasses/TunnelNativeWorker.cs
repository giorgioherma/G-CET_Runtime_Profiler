namespace GCETRuntimeProfiler.Core.Services;
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyTunnelNativeWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua", "local function __gcetTunnelAttachNative()", "local function __gcetTunnelFarOutside()");
        const string marker="-- G-CET semantic:tunnel-native-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Tunnel native worker already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var text=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string old="lookup:attachCompiled(10032,2,function() __gcetTunnelNativeOutside=false end,\n              function() __gcetTunnelNativeOutside=true end,\n              function() __gcetTunnelNativeOutside=nil;return true end)";
        if(text.Split(old,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Tunnel tri-state native owner/source has changed.");
        text=text.Replace(old,
            marker+"\n            require(\"GCET_Semantic/tunnel_native_worker\").attach(lookup,\n              function(near) __gcetTunnelNativeOutside=not near end,\n              function() __gcetTunnelNativeOutside=nil;return true end)",
            StringComparison.Ordinal);
        context.Write(file,crlf?text.Replace("\n","\r\n",StringComparison.Ordinal):text);
        context.WriteCoordinator("tunnel_native_worker.lua", """
-- G-CET semantic coordinator:tunnel-native-worker
-- Native wake registration delegated to 0-Engine; quest work remains local.
local M = {}
function M.attach(host, onKnown, onUnknown)
  if not host or type(host.attachCompiled) ~= "function" then return false end
  local ok, attached = pcall(function()
    return host:attachCompiled(10032, 2,
      function() onKnown(true) end,
      function() onKnown(false) end,
      onUnknown)
  end)
  return ok and attached == true
end
return M
""");
        return SemanticInjectionResult.Success(
            "Tunnel two-site native wake moved to coordinator; quest safeguards and UNKNOWN distance calculation unchanged.");
    }
}
