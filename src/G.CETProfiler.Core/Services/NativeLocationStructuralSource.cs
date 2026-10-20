using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Source-structural native-location hook adapters. All checks are local to
/// an independently replaceable behavior stage; mod version and SHA never
/// decide admission. A missing/ambiguous source graph refuses ONLY this hook.
/// Geometry is independently checked by CompiledNativeLocations.
/// </summary>
internal static class NativeLocationStructuralSource
{
    private static readonly RegexOptions Opt =
        RegexOptions.CultureInvariant | RegexOptions.Multiline;

    private static Match Unique(string text,string expression,string stage)
    {
        var matches=Regex.Matches(text,expression,Opt);
        if(matches.Count!=1)
            throw new InvalidOperationException(
                $"{stage}: expected one proven source shape; found {matches.Count}.");
        return matches[0];
    }
    private static string ReplaceOne(string text,string old,string replacement,
                                     string stage)
    {
        if(string.IsNullOrEmpty(old) ||
           text.Split(old,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException($"{stage}: ambiguous source edge.");
        return text.Replace(old,replacement,StringComparison.Ordinal);
    }
    private static string Normalize(string text,out bool crlf)
    {
        crlf=text.Contains("\r\n",StringComparison.Ordinal);
        return text.Replace("\r\n","\n",StringComparison.Ordinal);
    }
    private static string Restore(string text,bool crlf) =>
        crlf?text.Replace("\n","\r\n",StringComparison.Ordinal):text;

    // Used for semantic candidate admission and per-stage completion. These
    // are source *graphs* (registration + lifecycle + consumer), not hashes,
    // version tags, changed logger text, or identity-only filename matches.
    // The injector subsequently verifies unique exact mutation edges.
    internal static bool ProvesSourceShape(string rule,IEnumerable<string> sources)
    {
        foreach(var text in sources)
        {
            bool Has(string literal)=>text.Contains(literal,StringComparison.Ordinal);
            switch(rule)
            {
                case "native-location-poker":
                    if(Has("StreetTables.update") && Has("StreetTables.locations") &&
                       Has("poker.world_far") && Has("poker.world_near") &&
                       Has("poker.seat_near") && Has("Schedule.EveryFrame") &&
                       Regex.IsMatch(text,@"\bclient\.Schedule\.Every\s*\(",Opt) &&
                       Regex.IsMatch(text,@"\b[A-Za-z_]\w*\.Pause\(\)",Opt) &&
                       Regex.IsMatch(text,@"\b[A-Za-z_]\w*\.Resume\(\)",Opt))
                        return true;
                    break;
                case "native-location-dedka":
                    if(Has("dedka_entrance1") && Has("dedka_entrance2_cluster") &&
                       Has("RegisterZone(") && Has("startShopDetail()") &&
                       Has("stopShopDetail()") &&
                       Regex.IsMatch(text,@"\bonEnter\s*=",Opt) &&
                       Regex.IsMatch(text,@"\bonExit\s*=",Opt))
                        return true;
                    break;
                case "native-location-marmurbank":
                    if(Has("PosData.All") && Has("RegisterSpatialSet") &&
                       Has("QueryNearest") && Has("spatialHandle:unregister()") &&
                       Regex.IsMatch(text,
                           @"function\s+bank:registerSpatialSet\s*\(",Opt) &&
                       Regex.IsMatch(text,@"function\s+bank:distanceListener\s*\(",Opt))
                        return true;
                    break;
            }
        }
        return false;
    }

    internal static bool CompleteStage(string rule,IEnumerable<string> sources)
    {
        foreach(var t in sources)
        {
            switch(rule)
            {
                case "native-location-poker":
                    if(t.Contains("G-CET native location:poker-structural-v1",StringComparison.Ordinal) &&
                       t.Contains("lookup:attachCompiled(10031",StringComparison.Ordinal) &&
                       t.Contains("__gcetPokerNativeOwned",StringComparison.Ordinal) &&
                       t.Contains("farWorld.Resume()",StringComparison.Ordinal))
                        return true;
                    break;
                case "native-location-dedka":
                    if(t.Contains("G-CET native location:dedka-structural-v1",StringComparison.Ordinal) &&
                       t.Contains("lookup:attachCompiled(10034",StringComparison.Ordinal) &&
                       t.Contains("__gcetDedkaRestore",StringComparison.Ordinal) &&
                       t.Contains("__gcetDedkaRetire",StringComparison.Ordinal))
                        return true;
                    break;
                case "native-location-marmurbank":
                    if(t.Contains("G-CET native location:marmurbank-structural-v1",StringComparison.Ordinal) &&
                       t.Contains("lookup:attachCompiled(10036",StringComparison.Ordinal) &&
                       t.Contains("self.__gcetNativeFar",StringComparison.Ordinal) &&
                       t.Contains("self.spatialHandle:unregister()",StringComparison.Ordinal))
                        return true;
                    break;
            }
        }
        return false;
    }

    // Current PASS4 variants can rename handles, regroup declarations, and
    // reformat timers. Identify the three lanes by their stable scheduler IDs
    // AND prove pause/resume and active/realtime behavior before editing.
    internal static SemanticInjectionResult? TryPoker(SemanticPatchContext context)
    {
        SemanticTextFile file;
        try {
            file=context.FindFile("init.lua","poker.world_far","poker.world_near",
                "poker.seat_near","StreetTables.update");
        } catch(InvalidOperationException) {return null;}
        if(file.Text.Contains("G-CET native location:poker-",StringComparison.Ordinal))
            return null; // Legacy source has its own complete v1->v2 path.
        var t=Normalize(file.Text,out var crlf);
        Match far,near,seat,mode,ready,active,commit;
        try {
            far=Unique(t,@"(?<v>[A-Za-z_]\w*)\s*=\s*client\.Schedule\.Every\s*\([^\n]*\bid\s*=\s*[""']poker\.world_far[""']",
                "Poker far lane");
            near=Unique(t,@"(?<v>[A-Za-z_]\w*)\s*=\s*client\.Schedule\.Every\s*\([^\n]*\bid\s*=\s*[""']poker\.world_near[""']",
                "Poker near lane");
            seat=Unique(t,@"(?<v>[A-Za-z_]\w*)\s*=\s*client\.Schedule\.Every\s*\([^\n]*\bid\s*=\s*[""']poker\.seat_near[""']",
                "Poker seat lane");
            mode=Unique(t,@"local\s+function\s+(?<v>[A-Za-z_]\w*)\s*\(\s*state\s*\)[\s\S]*?(?=\n\s*[A-Za-z_]\w*\s*=\s*client\.Schedule\.Every\s*\()",
                "Poker mode transitions");
            ready=Unique(t,@"local\s+function\s+(?<v>[A-Za-z_]\w*Ready)\s*\(", "Poker ready predicate");
            active=Unique(t,@"local\s+function\s+(?<v>[A-Za-z_]\w*Active)\s*\(", "Poker active predicate");
            commit=Unique(t,@"(?m)^(?<indent>[ \t]*)(?<v>__gcetPokerAttached)\s*=\s*true[ \t]*$",
                "Poker scheduler startup");
        } catch(InvalidOperationException) {return null;}
        var f=far.Groups["v"].Value;
        var n=near.Groups["v"].Value;
        var a=seat.Groups["v"].Value;
        var setter=mode.Groups["v"].Value;
        var readyFn=ready.Groups["v"].Value;
        var activeFn=active.Groups["v"].Value;
        var declared=Regex.Match(t,@"\blocal\s+nearMode\s*=\s*false\b",Opt);
        if(!declared.Success ||
           !t.Contains(f+".Pause()",StringComparison.Ordinal) ||
           !t.Contains(f+".Resume()",StringComparison.Ordinal) ||
           !t.Contains(n+".Pause()",StringComparison.Ordinal) ||
           !t.Contains(n+".Resume()",StringComparison.Ordinal) ||
           !t.Contains(a+".Pause()",StringComparison.Ordinal) ||
           !t.Contains("Schedule.EveryFrame",StringComparison.Ordinal) ||
           !t.Contains("InteractionMenu.visible",StringComparison.Ordinal) ||
           !t.Contains("StreetTables.locations",StringComparison.Ordinal))
            return null;
        // Do not edit a second equivalent guard or a function outside the
        // proven scheduler. Failure leaves the original scanner untouched.
        try {
            var resume=Unique(t,@"if\s+"+Regex.Escape(f)+@"\s+then\s*"+
                Regex.Escape(f)+@"\.Resume\(\)\s*end",
                "Poker far-resume edge");
            var startup=commit.Groups["indent"].Value;
            t=ReplaceOne(t,declared.Value,
                declared.Value+"\n"+startup+"local __gcetPokerNativeNear=false\n"+
                startup+"local __gcetPokerNativeOwned=false",
                "Poker native state");
            t=ReplaceOne(t,resume.Value,
                "if "+f+" then if __gcetPokerNativeOwned then "+f+
                ".Pause() else "+f+".Resume() end end",
                "Poker source far-resume preservation");
            // An initial native NEAR during a load screen must rearm discovery
            // when the already-existing realtime lane becomes ready.
            var activeLine=Unique(t,@"(?m)^(?<indent>[ \t]*)local\s+active\s*=\s*"+
                Regex.Escape(activeFn)+@"\(\)[ \t]*$", "Poker realtime active edge");
            t=ReplaceOne(t,activeLine.Value,
                activeLine.Value+"\n"+activeLine.Groups["indent"].Value+
                "if __gcetPokerNativeOwned and __gcetPokerNativeNear and not nearMode then "+
                setter+"(true) end", "Poker native wake on first ready frame");
            var block=$$"""
            -- G-CET native location:poker-structural-v1
            -- Original 1Hz far scanner is standby until the native owner has
            -- an authoritative snapshot. Active hand/seat/realtime stay as-is.
            local lookup=engine.GCET and engine.GCET.NativeLocationHost
            if lookup and type(lookup.attachCompiled)=="function" then
              lookup:attachCompiled(10031,#StreetTables.locations,
                function()
                  __gcetPokerNativeOwned=true
                  __gcetPokerNativeNear=true
                  if {{readyFn}}() then {{setter}}(true) end
                  if {{f}} then {{f}}.Pause() end
                end,
                function()
                  __gcetPokerNativeOwned=true
                  __gcetPokerNativeNear=false
                  if {{readyFn}}() then
                    {{setter}}({{activeFn}}() or InteractionMenu.visible)
                  end
                  if {{f}} then {{f}}.Pause() end
                end,
                function()
                  __gcetPokerNativeOwned=false
                  __gcetPokerNativeNear=false
                  if {{f}} then {{f}}.Resume() end
                  return true
                end)
            end
            """;
            t=ReplaceOne(t,commit.Value,block+"\n"+commit.Value,
                "Poker native binding");
            context.Write(file,Restore(t,crlf));
            return SemanticInjectionResult.Success(
                "Source-proven Poker PASS4 lanes reconnected to compiled static locations; original far polling is fallback, near/seat/active work unchanged.");
        }catch(InvalidOperationException){return null;}
    }

    // Discover author zone helper by both call sites, then prove the *same*
    // function's registration and exit/entry callbacks. Never assume an older
    // expanded RegisterZone template or require the previous variable names.
    internal static SemanticInjectionResult? TryDedka(SemanticPatchContext context)
    {
        SemanticTextFile file;
        try {
            file=context.FindFile("init.lua","dedka_entrance1",
                "dedka_entrance2_cluster","RegisterZone(","startShopDetail",
                "stopShopDetail");
        }catch(InvalidOperationException){return null;}
        if(file.Text.Contains("G-CET native location:dedka-",StringComparison.Ordinal))
            return null;
        var t=Normalize(file.Text,out var crlf);
        try {
            var one=Unique(t,@"(?m)^[ \t]*(?<fn>[A-Za-z_]\w*)\s*\(\s*[""']dedka_entrance1[""']\s*,\s*(?<pos>[A-Za-z_]\w*)\s*\)[ \t]*$",
                "Dedka first site");
            var two=Unique(t,@"(?m)^[ \t]*(?<fn>[A-Za-z_]\w*)\s*\(\s*[""']dedka_entrance2_cluster[""']\s*,\s*(?<pos>[A-Za-z_]\w*)\s*\)[ \t]*$",
                "Dedka second site");
            var fn=one.Groups["fn"].Value;
            if(fn!=two.Groups["fn"].Value ||
               one.Groups["pos"].Value==two.Groups["pos"].Value)
                return null;
            var helper=Unique(t,@"(?m)^[ \t]*local\s+function\s+"+
                Regex.Escape(fn)+@"\s*\(\s*(?<id>[A-Za-z_]\w*)\s*,\s*(?<pos>[A-Za-z_]\w*)\s*\)",
                "Dedka zone helper");
            if(helper.Index>one.Index || one.Index>two.Index)
                return null;
            var helperBody=t[helper.Index..one.Index];
            var call=Unique(helperBody,
                @"(?m)^(?<ws>[ \t]*)(?<engine>[A-Za-z_]\w*)\.RegisterZone\s*\(\s*\{\s*",
                "Dedka author zone registration");
            if(!helperBody.Contains("radius",StringComparison.Ordinal) ||
               !Regex.IsMatch(helperBody,@"\bradius\s*=\s*200(?:\.0)?\b",Opt) ||
               !helperBody.Contains("onEnter",StringComparison.Ordinal) ||
               !helperBody.Contains("onExit",StringComparison.Ordinal) ||
               !helperBody.Contains("startShopDetail()",StringComparison.Ordinal) ||
               !helperBody.Contains("stopShopDetail()",StringComparison.Ordinal))
                return null;
            var engine=call.Groups["engine"].Value;
            // A handle is needed to retire and later restore the author's two
            // zones. Return the exact existing registration's result.
            t=ReplaceOne(t,call.Value,
                call.Groups["ws"].Value+"return "+engine+".RegisterZone({",
                "Dedka author zone handle");
            t=ReplaceOne(t,one.Value,
                "local __gcetDedkaNativeZone1="+one.Value.Trim(),
                "Dedka zone1 source ownership");
            t=ReplaceOne(t,two.Value,
                "local __gcetDedkaNativeZone2="+two.Value.Trim(),
                "Dedka zone2 source ownership");
            var insert=Unique(t,@"(?m)^[ \t]*local\s+__gcetDedkaNativeZone2\s*=\s*"+
                Regex.Escape(fn)+@"\s*\([^\n]+\)[ \t]*$",
                "Dedka native subscription position");
            var p1=one.Groups["pos"].Value;
            var p2=two.Groups["pos"].Value;
            var block=$$"""
            -- G-CET native location:dedka-structural-v1
            -- Keep author zones until native reports an authoritative state.
            local function __gcetDedkaRetire()
              for _,key in ipairs({"__gcetDedkaNativeZone1","__gcetDedkaNativeZone2"}) do
                local handle=(key=="__gcetDedkaNativeZone1") and __gcetDedkaNativeZone1 or __gcetDedkaNativeZone2
                if handle then
                  if type(handle.unregister)~="function" then return false end
                  local ok=pcall(handle.unregister)
                  if not ok then return false end
                  if key=="__gcetDedkaNativeZone1" then __gcetDedkaNativeZone1=nil
                  else __gcetDedkaNativeZone2=nil end
                end
              end
              return true
            end
            local function __gcetDedkaRestore()
              if not __gcetDedkaNativeZone1 then
                __gcetDedkaNativeZone1={{fn}}("dedka_entrance1",{{p1}})
              end
              if not __gcetDedkaNativeZone2 then
                __gcetDedkaNativeZone2={{fn}}("dedka_entrance2_cluster",{{p2}})
              end
              return __gcetDedkaNativeZone1~=nil and __gcetDedkaNativeZone2~=nil
            end
            local lookup={{engine}}.GCET and {{engine}}.GCET.NativeLocationHost
            if lookup and type(lookup.attachCompiled)=="function" then
              lookup:attachCompiled(10034,2,
                function()
                  if not __gcetDedkaRetire() then return false end
                  shopNearCount=1;startShopDetail()
                end,
                function()
                  if not __gcetDedkaRetire() then return false end
                  shopNearCount=0;stopShopDetail()
                end,
                function() return __gcetDedkaRestore() end)
            end
            """;
            t=ReplaceOne(t,insert.Value,insert.Value+"\n"+block,
                "Dedka source zone callbacks");
            context.Write(file,Restore(t,crlf));
            return SemanticInjectionResult.Success(
                "Compiled Dedka owner adopts structurally proven two author zone registrations; independent near detail and native fallback retained.");
        }catch(InvalidOperationException){return null;}
    }

    // The banker's logger, helper positions, and early return wording have no
    // bearing on its spatial lifecycle. Admit the FAR gate independently when
    // the register/unregister/query lifecycle is structurally complete.
    internal static SemanticInjectionResult? TryMarmur(SemanticPatchContext context)
    {
        SemanticTextFile file;
        try {
            file=context.FindFile("module/Bank.lua",
                "function bank:registerSpatialSet()","PosData.All",
                "RegisterSpatialSet","function bank:distanceListener()",
                "QueryNearest");
        }catch(InvalidOperationException){return null;}
        if(file.Text.Contains("G-CET native location:marmurbank-",StringComparison.Ordinal))
            return null;
        var t=Normalize(file.Text,out var crlf);
        try {
            var start=Unique(t,@"(?m)^function\s+bank:registerSpatialSet\s*\(\s*\)[ \t]*$",
                "Banker spatial registration");
            var end=t.IndexOf("\nend",start.Index+start.Length,StringComparison.Ordinal);
            if(end<0)return null;
            var body=t[start.Index..end];
            if(!Regex.IsMatch(body,
                @"self\.spatialHandle\s*=\s*self\.engine\.RegisterSpatialSet\s*\(\s*[""']marmurbank[""']\s*,\s*PosData\.All",Opt) ||
                !body.Contains("self.spatialHandle:unregister()",StringComparison.Ordinal) ||
                !body.Contains("self.engine",StringComparison.Ordinal))
                return null;
            var guard=Unique(body,@"(?m)^[ \t]*if\s+not\s+self\.engine\s+then\s+return\s+end[ \t]*$",
                "Banker engine readiness");
            var listener=Unique(t,@"(?m)^function\s+bank:distanceListener\s*\(\s*\)[ \t]*$",
                "Banker distance listener");
            var next=t.IndexOf("\nend",listener.Index+listener.Length,StringComparison.Ordinal);
            if(next<0)return null;
            var listenerBody=t[listener.Index..next];
            if(!listenerBody.Contains("QueryNearest",StringComparison.Ordinal) ||
               !listenerBody.Contains("marmurbank",StringComparison.Ordinal))
                return null;
            // Change only the proven registration function: unrelated
            // methods may use the same engine-readiness guard.
            var rewritten=body.Replace(guard.Value,
                guard.Value+"\n\tif self.__gcetNativeFar then return end",
                StringComparison.Ordinal);
            if(rewritten==body)
                throw new InvalidOperationException("Banker guard did not change.");
            t=t[..start.Index]+rewritten+t[end..];
            // Insert inside the proved registration function, before its
            // closing 'end'; no logger text or pretty-printing dependence.
            var endAfter=t.IndexOf("\nend",start.Index+start.Length,StringComparison.Ordinal);
            var insertion="""

  -- G-CET native location:marmurbank-v1
  -- G-CET native location:marmurbank-structural-v1
  if not self.__gcetNativeAttached then
    local lookup=self.engine.GCET and self.engine.GCET.NativeLocationHost
    if lookup and type(lookup.attachCompiled)=="function" then
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
""";
            t=t.Insert(endAfter,insertion);
            t=ReplaceOne(t,listener.Value,
                listener.Value+"\n\tif self.__gcetNativeFar then self.nearestLocation=nil;return 0 end",
                "Banker nearest-query FAR");
            context.Write(file,Restore(t,crlf));
            return SemanticInjectionResult.Success(
                "Banker native FAR stage accepted after independent registration, unregister and nearest-query structural proof.");
        }catch(InvalidOperationException){return null;}
    }
}
