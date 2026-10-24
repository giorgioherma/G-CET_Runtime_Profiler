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
        var native="""
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

""".Replace("\r\n","\n",StringComparison.Ordinal);
        text=Once(text,"registerForEvent(\"onInit\", function()",
            native+"registerForEvent(\"onInit\", function()",
            "Card native registration helper placement");
        text=Once(text,
            "  Mod.Subscribe(\"PlayerReady\", registerSpatialSet)",
            "  __gcetAttachCardNative()\n  Mod.Subscribe(\"PlayerReady\", registerSpatialSet)",
            "Card native initial registration");
        context.Write(file,useCrlf ? text.Replace("\n","\r\n",StringComparison.Ordinal) : text);
        SemanticInjectors.ApplyCardCollectionNativeWorker(context);
        return SemanticInjectionResult.Success(
            "Native 100-site aggregate FAR parks the original 0-Engine card spatial set; NEAR/UNKNOWN restores exact 2m author collection callbacks and picked-state behavior.");
    }

    internal static SemanticInjectionResult ApplyMarmur(SemanticPatchContext context)
    {
        var structural=NativeLocationStructuralSource.TryMarmur(context);
        if(structural is not null)return structural;
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
        var anchor="""
	if self.spatialHandle then
		self.engine.Log("Marmurbank",
			string.format("Registered %d banker locations with 0-Engine SpatialHash", self.spatialHandle:count()), "info")
	end
""".Replace("\r\n","\n",StringComparison.Ordinal);
        var addition="""
	if not self.__gcetNativeAttached then
		local lookup=self.engine.GCET and self.engine.GCET.NativeLocationHost
		if lookup and type(lookup.attachCompiled)=="function" then
			if #PosData.All>0 then
				self.__gcetNativeAttached=lookup:attachCompiled(10036,#PosData.All,
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
""".Replace("\r\n","\n",StringComparison.Ordinal);
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


    // Existing v5 Ziggy offered/far dormancy: exact 130 m static giver site.
    // Native replaces only that distance decision; live quest, player/session
    // identity, dialogue, journal, audio and render cadence remain untouched.
    internal static SemanticInjectionResult ApplyZiggy(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local __gcetZiggyOfferedFarAcc=0.0",
            "local function __gcetZiggyOfferedFar()",
            "H.s.phase=='offered'",
            "C.giver and C.giver.position",
            "return dx*dx+dy*dy+dz*dz>130*130",
            "H.hooks:Register()");
        const string marker="G-CET native location:ziggy-offered-v1";
        if (file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Native Ziggy offered/far wake already installed.");
        var useCrlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var text=useCrlf ? file.Text.Replace("\r\n","\n",StringComparison.Ordinal) : file.Text;

        const string setup="""
-- G-CET native location:ziggy-offered-v1
-- Native is authoritative only after a complete snapshot. nil means use the
-- source's exact previous position/math probe, including quest safety guards.
local __gcetZiggyNativeOfferedFar=nil
local function __gcetZiggyAttachNative()
 local ok,engine=pcall(GetMod,'0-Engine')
 local lookup=ok and engine and engine.GCET and engine.GCET.NativeLocationHost
 local loc=C.giver and C.giver.position
 if not lookup or type(lookup.attachCompiled)~='function' or
    type(loc)~='table' then return end
 lookup:attachCompiled(10038,1,function() __gcetZiggyNativeOfferedFar=false end,
   function() __gcetZiggyNativeOfferedFar=true end,
   function() __gcetZiggyNativeOfferedFar=nil;return true end)
end

""";
        text=Once(text,
            "registerForEvent('onInit',function()H.hooks=NewObject('ZG11Hooks');H.hooks:Register();",
            setup+"registerForEvent('onInit',function()H.hooks=NewObject('ZG11Hooks');H.hooks:Register();__gcetZiggyAttachNative();",
            "Ziggy source-owned native registration on CET init");

        // The getter and original squared-distance check are retained exactly
        // as the fail-open source path. The player's identity/session guards
        // execute before native can return either known NEAR or known FAR.
        text=Once(text,
            " if tostring(p:GetEntityID().hash)~=H.playerKey then return false end\n local pos=p:GetWorldPosition()",
            " if tostring(p:GetEntityID().hash)~=H.playerKey then return false end\n"+
            " if __gcetZiggyNativeOfferedFar ~= nil then return __gcetZiggyNativeOfferedFar end\n"+
            " local pos=p:GetWorldPosition()",
            "Ziggy native offered distance substitution after source safety guards");
        context.Write(file,useCrlf ? text.Replace("\n","\r\n",StringComparison.Ordinal) : text);
        SemanticInjectors.ApplyZiggyNativeWorker(context);
        return SemanticInjectionResult.Success(
            "Existing Ziggy 130m offered-state FAR/NEAR probe now uses shared native transitions after player/session/dialogue guards; original math remains for UNKNOWN and 1Hz/active quest cadence is unchanged.");
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
            "    if lookup and type(lookup.attachCompiled)==\"function\" then\n"+
            "        -- The eye-height corrected anchor is already in GCAS.\n"+
            "        lookup:attachCompiled(10037,1,function() __gcetBathNativeFar=false end,\n"+
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
        SemanticInjectors.ApplyYaibaNativeWorker(context);
        return SemanticInjectionResult.Success(
            "Native FAR replaces the existing dormant bathtub vector arithmetic; icon/hub/water active state and original UNKNOWN distance check remain unchanged.");
    }
}
