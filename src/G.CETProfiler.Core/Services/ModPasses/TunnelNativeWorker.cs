using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyTunnelNativeWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua", "local function __gcetTunnelAttachNative()", "local function __gcetTunnelFarOutside()");
        const string marker="-- G-CET semantic:tunnel-native-worker-v1";
        if(file.Text.Contains("GCET_Semantic/tunnel_native_worker",StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Tunnel native worker bridge already installed.");
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            throw new InvalidOperationException("Tunnel worker marker exists without its source bridge.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var text=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        // Only the proven three-state native registration moves into a worker.
        // Whitespace and harmless Lua comments cannot change its semantics.
        const string gap = @"(?:\s|--[^\r\n]*(?:\r?\n|$))*";
        var pattern = string.Join(gap, new[]
        {
            @"lookup", @":", @"attachCompiled", @"\(", @"10032", @",", @"2", @",",
            @"function", @"\(", @"\)", @"__gcetTunnelNativeOutside", @"=", @"false", @"end", @",",
            @"function", @"\(", @"\)", @"__gcetTunnelNativeOutside", @"=", @"true", @"end", @",",
            @"function", @"\(", @"\)", @"__gcetTunnelNativeOutside", @"=", @"nil", @";?",
            @"return", @"true", @"end", @"\)"
        });
        var registration = new Regex(pattern, RegexOptions.CultureInvariant);
        var matches = registration.Matches(text);
        var ownerCalls = Regex.Matches(text,
            "lookup" + gap + ":" + gap + "attachCompiled" + gap + @"\(",
            RegexOptions.CultureInvariant);
        var attachStart = text.IndexOf("local function __gcetTunnelAttachNative()",
            StringComparison.Ordinal);
        var onInit = attachStart >= 0
            ? text.IndexOf("registerForEvent", attachStart, StringComparison.Ordinal)
            : -1;
        if(matches.Count != 1 || ownerCalls.Count != 1 ||
           attachStart < 0 || matches[0].Index < attachStart ||
           (onInit >= 0 && matches[0].Index > onInit))
            throw new InvalidOperationException("Tunnel tri-state native owner/source has changed.");

        text = registration.Replace(text,
            marker + "\n            require(\"GCET_Semantic/tunnel_native_worker\").attach(lookup,\n" +
            "              function(near) __gcetTunnelNativeOutside=not near end,\n" +
            "              function() __gcetTunnelNativeOutside=nil;return true end)", 1);
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
