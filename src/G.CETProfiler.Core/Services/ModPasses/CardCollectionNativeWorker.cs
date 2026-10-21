namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyCardCollectionNativeWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua","local function __gcetAttachCardNative()",
            "local function registerSpatialSet()", "local function unregisterSpatialSet()");
        const string marker="-- G-CET semantic:card-collection-native-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Card Collection native coordinator already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var source=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string original="""
local function __gcetAttachCardNative()
  local lookup=engine and engine.GCET and engine.GCET.NativeLocationHost
  if not lookup or type(lookup.attachCompiled)~="function" then return end
  -- All 100 source-defined card anchors were compiled by Resolver.
  -- Keep exact collection and picked-state logic in Lua.
  if #buildSpatialEntries()==0 then return end
  lookup:attachCompiled(10035,100,
    function()
      __gcetCardNativeFar=false
      if engine.IsPlaying() then registerSpatialSet() end
    end,
    function()
      __gcetCardNativeFar=true
      unregisterSpatialSet()
    end,
    function()
      __gcetCardNativeFar=false
      if engine.IsPlaying() then registerSpatialSet() end
      return true
    end)
end

""";
        if(source.Split(original.TrimEnd('\n'),StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Card Collection native subscription source changed.");
        const string bridge="""
-- G-CET semantic:card-collection-native-worker-v1
local function __gcetAttachCardNative()
  return require("GCET_Semantic/card_collection_native_worker").attach(
    engine, (#buildSpatialEntries() > 0) and 100 or 0,
    function(far)
      __gcetCardNativeFar = far == true
      if __gcetCardNativeFar then
        unregisterSpatialSet()
      elseif engine.IsPlaying() then
        registerSpatialSet()
      end
    end)
end

""";
        source=source.Replace(original.TrimEnd('\n'),bridge.TrimEnd('\n'),StringComparison.Ordinal);
        context.Write(file,crlf?source.Replace("\n","\r\n",StringComparison.Ordinal):source);
        context.WriteCoordinator("card_collection_native_worker.lua", """
-- G-CET semantic coordinator:card-collection-native-worker
-- 0-Engine native subscription only; source owns card states and exact spatial set.
local M = {}
function M.attach(engine, count, onTransition)
  if type(engine) ~= "table" or type(count) ~= "number"
      or count == 0 or type(onTransition) ~= "function" then return false end
  local host = engine.GCET and engine.GCET.NativeLocationHost
  if not host or type(host.attachCompiled) ~= "function" then return false end
  local ok, attached = pcall(function()
    return host:attachCompiled(10035, count,
      function() onTransition(false) end,
      function() onTransition(true) end,
      function() onTransition(false); return true end)
  end)
  return ok and attached == true
end
return M
""");
        return SemanticInjectionResult.Success(
            "Card Collection native source callbacks moved to separate 0-Engine worker; gameplay, pickup and spatial registration remain source-owned.");
    }
}
