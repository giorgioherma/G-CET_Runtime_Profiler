namespace GCETRuntimeProfiler.Core.Services;
internal static partial class SemanticInjectors
{
    internal static SemanticInjectionResult ApplyIllegalMechanicNativeWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("physical_npc.lua","G-CET native location:illegal-v3","state.mechanics = result");
        const string marker="-- G-CET semantic:illegal-mechanic-native-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Illegal Mechanic native worker already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var s=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string begin="  -- G-CET native location:illegal-v3";
        const string finish="  log(\"matrix loaded mechanics=";
        var a=s.IndexOf(begin,StringComparison.Ordinal);
        var b=a<0?-1:s.IndexOf(finish,a+begin.Length,StringComparison.Ordinal);
        if(a<0||b<0||s.IndexOf(begin,a+begin.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("Illegal Mechanic v3 worker insertion boundary changed.");
        var native=s[a..b];
        foreach(var proof in new[]{"host:attachCompiled(1003301+index,1,","index>=0 and index<256",
            "entry.__gcetNativeFar=false;state.nextUpdateAt=0","entry.__gcetNativeFar=true;state.nextUpdateAt=0",
            "entry.__gcetNativeFar=false;state.nextUpdateAt=0;return true"})
            if(!native.Contains(proof,StringComparison.Ordinal))
                throw new InvalidOperationException("Illegal Mechanic native transition shape changed: "+proof);
        if(native.Split("host:attachCompiled(",StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Illegal Mechanic expected one attach expression.");
        const string bridge="""
  -- G-CET native location:illegal-v3
  -- G-CET semantic:illegal-mechanic-native-worker-v1
  -- Each native site remains independent. Missing native owners are UNKNOWN.
  require("GCET_Semantic/illegal_mechanic_native_worker").attach(result, state)

""";
        s=s[..a]+bridge+s[b..];
        context.Write(file,crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s);
        context.WriteCoordinator("illegal_mechanic_native_worker.lua", """
-- G-CET semantic coordinator:illegal-mechanic-native-worker
-- No scheduler: 0-Engine native host provides per-mechanic subscriptions.
local M = {}
function M.attach(entries, state)
  local engineOk, engine = pcall(GetMod, "0-Engine")
  local host = engineOk and engine and engine.GCET and engine.GCET.NativeLocationHost
  if not host or type(host.attachCompiled) ~= "function" then return false end
  local anyAttached = false
  for _, entry in ipairs(entries) do
    local index = tonumber(entry.index)
    if index and index >= 0 and index < 256 then
      local ok, attached = pcall(function()
        return host:attachCompiled(1003301 + index, 1,
          function() entry.__gcetNativeFar = false; state.nextUpdateAt = 0 end,
          function() entry.__gcetNativeFar = true; state.nextUpdateAt = 0 end,
          function() entry.__gcetNativeFar = false; state.nextUpdateAt = 0; return true end)
      end)
      if ok and attached == true then anyAttached = true end
    end
  end
  return anyAttached
end
return M
""".Replace("-- G-CET semantic coordinator:illegal-mechanic-native-worker\n",
        "-- G-CET semantic coordinator:" + context.RuleId + "\n", StringComparison.Ordinal));
        return SemanticInjectionResult.Success(
            "Moved Illegal Mechanic v3 independent per-site native subscriptions to coordinator; exact NPC spawn/despawn, pending and UNKNOWN logic remain authored.");
    }
}
