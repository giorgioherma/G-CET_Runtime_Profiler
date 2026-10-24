namespace GCETRuntimeProfiler.Core.Services;

// The Poker source retains the near/seat/hand lifecycle callbacks and
// first-NEAR readiness reconciliation. 0-Engine owns the native location
// subscription, now invoked from a separately generated worker.
internal static partial class SemanticInjectors
{
    internal static SemanticInjectionResult ApplyPokerNativeWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua", "poker.world_far", "poker.world_near",
            "poker.seat_near", "StreetTables.locations");
        const string marker="-- G-CET semantic:poker-native-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Poker native coordinator already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var s=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string old="lookup:attachCompiled(10031,#StreetTables.locations,";
        if(s.Split(old,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Poker native location owner 10031 is absent or ambiguous.");
        var pos=s.IndexOf(old,StringComparison.Ordinal);
        var tail=s[pos..];
        // Both source-proven Poker v2 layouts use exactly three callbacks.
        // A changed callback shape remains an explicit semantic-review case.
        var end=tail.IndexOf("end)",StringComparison.Ordinal);
        if(end<0 || end>2200)
            throw new InvalidOperationException("Poker native callback boundary changed.");
        var bound=tail[..(end+4)];
        if(bound.Split("function()",StringSplitOptions.None).Length!=4 ||
           !bound.Contains("__gcetNativeOwned",StringComparison.Ordinal) &&
           !bound.Contains("__gcetPokerNativeOwned",StringComparison.Ordinal) ||
           !bound.Contains("return true",StringComparison.Ordinal))
            throw new InvalidOperationException("Poker native wake or fallback callbacks changed.");
        s=s.Replace(old,marker+"\n              require(\"GCET_Semantic/poker_native_worker\").attach(lookup,#StreetTables.locations,",
            StringComparison.Ordinal);
        context.Write(file,crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s);
        context.WriteCoordinator("poker_native_worker.lua", """
-- G-CET semantic coordinator:poker-native-worker
-- The host remains 0-Engine. Source-owned callbacks maintain every game state.
local M = {}
function M.attach(host, count, onNear, onFar, onUnknown)
  if type(host) ~= "table" or type(host.attachCompiled) ~= "function"
      or type(count) ~= "number" or count < 1
      or type(onNear) ~= "function" or type(onFar) ~= "function"
      or type(onUnknown) ~= "function" then return false end
  local ok, attached = pcall(function()
    return host:attachCompiled(10031, count, onNear, onFar, onUnknown)
  end)
  return ok and attached == true
end
return M
""".Replace("-- G-CET semantic coordinator:poker-native-worker\n",
        "-- G-CET semantic coordinator:" + context.RuleId + "\n", StringComparison.Ordinal));
        return SemanticInjectionResult.Success(
            "Poker owner 10031 native attach delegated to generated Lua worker; author NEAR, FAR, UNKNOWN, active hand, near-seat and fallback lifecycle retained.");
    }
}
