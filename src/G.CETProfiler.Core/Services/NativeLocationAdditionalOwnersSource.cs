namespace GCETRuntimeProfiler.Core.Services;

// Reuses the already-shipped source-owned location rules. Native owns only
// coarse wake; original 0-Engine spatial evaluation or author UI controls
// all exact interaction, collection, quest and lifetime semantics.
internal static class NativeLocationAdditionalOwnersSource
{
    private static string Once(string text, string oldValue, string newValue, string label)
    {
        var first=text.IndexOf(oldValue,StringComparison.Ordinal);
        if (first<0 || text.IndexOf(oldValue,first+oldValue.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException(label+": location source anchor changed or ambiguous.");
        return text[..first]+newValue+text[(first+oldValue.Length)..];
    }

    internal static SemanticInjectionResult ApplyCards(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua","local function buildSpatialEntries()",
            "local function collectCard(entry)","local function registerSpatialSet()",
            "Mod.RegisterSpatialSet(\"card_collection\"","local function unregisterSpatialSet()");
        const string marker="G-CET native location:card-collection-v1";
        if (file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Native card collection coarse wake already installed.");
        var useCrlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var text=useCrlf ? file.Text.Replace("\r\n","\n",StringComparison.Ordinal) : file.Text;
        text=Once(text,
            "local function registerSpatialSet()\n  if spatialHandle then return end",
            "-- "+marker+"\n"+
            "local __gcetCardNativeFar=false\n"+
            "local function registerSpatialSet()\n"+
            "  if __gcetCardNativeFar or spatialHandle then return end",
            "Card original spatial registration");
        const string native="""
local function __gcetAttachCardNative()
  local lookup=engine and engine.GCET and engine.GCET.NativeLocationHost
  if not lookup or type(lookup.attach)~="function" then return end
  -- Reuse exactly the author's uncollected source entries. The 20m
  -- outer wake arms the original 1s poll before the 2m collection boundary.
  local sites={}
  for _,entry in ipairs(buildSpatialEntries()) do
    sites[#sites+1]={x=entry.x,y=entry.y,z=entry.z,radius=20.0,metric="xyz"}
  end
  if #sites==0 then return end
  lookup:attach(10035,sites,
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
        text=Once(text,"registerForEvent(\"onInit\", function()",
            native+"registerForEvent(\"onInit\", function()",
            "Card native registration helper placement");
        text=Once(text,
            "  Mod.Subscribe(\"PlayerReady\", registerSpatialSet)",
            "  __gcetAttachCardNative()\n  Mod.Subscribe(\"PlayerReady\", registerSpatialSet)",
            "Card native initial registration");
        context.Write(file,useCrlf ? text.Replace("\n","\r\n",StringComparison.Ordinal) : text);
        return SemanticInjectionResult.Success(
            "Native 100-site aggregate FAR parks the original 0-Engine card spatial set; NEAR/UNKNOWN restores exact 2m author collection callbacks and picked-state behavior.");
    }

    internal static SemanticInjectionResult ApplyMarmur(SemanticPatchContext context)
    {
        var file=context.FindFile("module/Bank.lua",
            "function bank:registerSpatialSet()","PosData.All",
            "self.engine.RegisterSpatialSet(\"marmurbank\"",
            "function bank:distanceListener()",
            "self.engine.QueryNearest(\"marmurbank\"");
        const string marker="G-CET native location:marmurbank-v1";
        if (file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Native banker coarse wake already installed.");
        var useCrlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var text=useCrlf ? file.Text.Replace("\r\n","\n",StringComparison.Ordinal) : file.Text;
        text=Once(text,
            "function bank:registerSpatialSet()\n\tif not self.engine then return end",
            "function bank:registerSpatialSet()\n"+
            "\t-- "+marker+"\n"+
            "\tif not self.engine or self.__gcetNativeFar then return end",
            "Banker spatial registration original guard");
        const string anchor="""
	if self.spatialHandle then
		self.engine.Log("Marmurbank",
			string.format("Registered %d banker locations with 0-Engine SpatialHash", self.spatialHandle:count()), "info")
	end
""";
        const string addition="""
	if not self.__gcetNativeAttached then
		local lookup=self.engine.GCET and self.engine.GCET.NativeLocationHost
		if lookup and type(lookup.attach)=="function" then
			local sites={}
			for _,loc in ipairs(PosData.All) do
				sites[#sites+1]={x=loc.x,y=loc.y,z=loc.z,radius=10.0,metric="xyz"}
			end
			if #sites>0 then
				self.__gcetNativeAttached=lookup:attach(10036,sites,
					function()
						self.__gcetNativeFar=false
						self:registerSpatialSet()
					end,
					function()
						self.__gcetNativeFar=true
						self.nearestLocation=nil
						if self.spatialHandle then
							self.spatialHandle:unregister()
							self.spatialHandle=nil
						end
					end,
					function()
						self.__gcetNativeFar=false
						self:registerSpatialSet()
						return true
					end)==true
			end
		end
	end
""";
        text=Once(text,anchor,anchor+addition,
            "Banker original spatial set and source-owned positions");
        text=Once(text,
            "function bank:distanceListener()\n\tif not self.engine then return 0 end",
            "function bank:distanceListener()\n"+
            "\tif self.__gcetNativeFar then self.nearestLocation=nil;return 0 end\n"+
            "\tif not self.engine then return 0 end",
            "Banker native FAR bypasses only idle QueryNearest");
        context.Write(file,useCrlf ? text.Replace("\n","\r\n",StringComparison.Ordinal) : text);
        return SemanticInjectionResult.Success(
            "Native coarse FAR parks the three banker SpatialHash entries and QueryNearest; NEAR/UNKNOWN restores the original exact 2m nearest interaction and banking UI.");
    }

    internal static SemanticInjectionResult ApplyBathtub(SemanticPatchContext context)
    {
        var file=context.FindFile("bathtub.lua",
            "function bathtub.init()","function bathtub.update(player, pPos)",
            "G-CET semantic:yaiba-penthouse-distant-bathtub",
            "__gcetDx * __gcetDx + __gcetDy * __gcetDy + __gcetDz * __gcetDz",
            "togglePin(near)","bathtub.waterApplied");
        const string marker="G-CET native location:yaiba-bathtub-v1";
        if (file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Native bathtub location gate already installed.");
        var useCrlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var text=useCrlf ? file.Text.Replace("\r\n","\n",StringComparison.Ordinal) : file.Text;
        text=Once(text,
            "function bathtub.init()\n    ui.init()",
            "-- "+marker+"\n"+
            "local __gcetBathNativeFar=nil\n"+
            "function bathtub.init()\n    ui.init()\n"+
            "    local ok,engine=pcall(GetMod,\"0-Engine\")\n"+
            "    local lookup=ok and engine and engine.GCET and engine.GCET.NativeLocationHost\n"+
            "    if lookup and type(lookup.attach)==\"function\" then\n"+
            "        -- Author eye z=pPos.z+1; translate anchor z to preserve threshold.\n"+
            "        lookup:attach(10037,{{x=CONFIG.position.x,y=CONFIG.position.y,\n"+
            "            z=CONFIG.position.z-1.0,radius=CONFIG.iconRange+1.0,metric=\"xyz\"}},\n"+
            "            function() __gcetBathNativeFar=false end,\n"+
            "            function() __gcetBathNativeFar=true end,\n"+
            "            function() __gcetBathNativeFar=nil;return true end)\n"+
            "    end",
            "Bathtub original init");
        text=Once(text,
            "    if player and pPos and not bathtub.hubVisible and not ui.hubShown and not bathtub.pinID and not bathtub.waterApplied then\n"+
            "        local __gcetBathPos = CONFIG.position",
            "    if player and pPos and not bathtub.hubVisible and not ui.hubShown and not bathtub.pinID and not bathtub.waterApplied then\n"+
            "        if __gcetBathNativeFar == true then return end\n"+
            "        if __gcetBathNativeFar == nil then\n"+
            "        local __gcetBathPos = CONFIG.position",
            "Bathtub prior dormant distance rule");
        text=Once(text,
            "        if __gcetDx * __gcetDx + __gcetDy * __gcetDy + __gcetDz * __gcetDz > (CONFIG.iconRange + 1.0)^2 then return end\n    end\n    ui.update()",
            "        if __gcetDx * __gcetDx + __gcetDy * __gcetDy + __gcetDz * __gcetDz > (CONFIG.iconRange + 1.0)^2 then return end\n"+
            "        end -- original distance check used only until first native snapshot / UNKNOWN\n"+
            "    end\n    ui.update()",
            "Bathtub fallback exact geometry retained");
        context.Write(file,useCrlf ? text.Replace("\n","\r\n",StringComparison.Ordinal) : text);
        return SemanticInjectionResult.Success(
            "Native FAR replaces the existing dormant bathtub vector arithmetic; icon/hub/water active state and original UNKNOWN distance check remain unchanged.");
    }
}
