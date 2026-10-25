namespace GCETRuntimeProfiler.Core.Services;

// Behavior stage: after generic 0-Engine and native inventory.
// The existing fixed-jump and vehicle callbacks remain author-owned.
internal static partial class SemanticInjectors
{
    private static string CyberOnce(string src,string anchor,string replacement,string label)
    {
        var pos=src.IndexOf(anchor,StringComparison.Ordinal);
        if(pos<0 || src.IndexOf(anchor,pos+anchor.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("CyberStunts "+label+
                ": source anchor absent or ambiguous.");
        return src[..pos]+replacement+src[(pos+anchor.Length)..];
    }

    private static string CyberRegexOnce(string src,string pattern,string replacement,
        string label)
    {
        var regex=new System.Text.RegularExpressions.Regex(pattern,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant |
            System.Text.RegularExpressions.RegexOptions.Multiline);
        if(regex.Matches(src).Count!=1)
            throw new InvalidOperationException("CyberStunts "+label+
                ": current source callback changed.");
        return regex.Replace(src,replacement,1);
    }

    private static SemanticInjectionResult ApplyCyberStuntsNativeLocation(
        SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua","local jumps = {","local userJumps = {}",
            "launchDetectionRadius","DetectJumpLaunch(","LoadUserJumps()",
            "SaveUserJumps()","DriveEvents","wasVehicleAirborne");
        const string marker="G-CET native location:cyberstunts-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
        {
            if(file.Text.Contains("attachCompiled(10039,",StringComparison.Ordinal) &&
                file.Text.Contains("__gcetStuntsLocationSleeping()",StringComparison.Ordinal))
                return SemanticInjectionResult.Skip("CyberStunts native FAR stage already installed.");
            throw new InvalidOperationException("Partial CyberStunts native stage; preserved.");
        }
        if(CompiledNativeLocations.CyberStuntsFixedJumpCount(file.Text)==0)
            throw new InvalidOperationException("Unproven current fixed-jump coordinate definitions.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var src=crlf?file.Text.Replace("\r\n","\n",StringComparison.Ordinal):file.Text;
        const string helpers="""
-- G-CET native location:cyberstunts-v1
-- UNKNOWN/native absent/custom jumps -> original callback logic.
local __gcetStuntsFixedCount=#jumps
local __gcetStuntsNativeFar=nil
local __gcetStuntsUserEdited=false
local function __gcetStuntsLocationSleeping()
    return __gcetStuntsNativeFar==true and not isJumping and
        not __gcetStuntsUserEdited and #jumps==__gcetStuntsFixedCount
end
local function __gcetStuntsAttachNative()
    if #jumps~=(__gcetStuntsFixedCount) then return end
    local ok,engine=pcall(GetMod,"0-Engine")
    local host=ok and engine and engine.GCET and engine.GCET.NativeLocationHost
    if host and type(host.attachCompiled)=="function" then
        host:attachCompiled(10039,__gcetStuntsFixedCount,
            function() __gcetStuntsNativeFar=false;wasVehicleAirborne=nil end,
            function() __gcetStuntsNativeFar=true;wasVehicleAirborne=nil end,
            function() __gcetStuntsNativeFar=nil;wasVehicleAirborne=nil;return true end)
    end
end

""";
        src=CyberRegexOnce(src,
            @"(?m)^(local\s+launchDetectionRadius\s*=\s*[-+0-9.eE]+)[ \t]*$",
            "$1\n"+helpers,"static fixed-jump state");
        src=CyberOnce(src,"function SaveUserJumps()\n",
            "function SaveUserJumps()\n    __gcetStuntsUserEdited=true\n",
            "custom jump invalidation");
        // Registration is AFTER author JSON/custom-jump loading at onInit.
        src=CyberOnce(src,
            "    ObserveAfter(\"inkMenusState\", \"OpenMenu\", function(this, menuName, userData)",
            "    __gcetStuntsAttachNative()\n"+
            "    ObserveAfter(\"inkMenusState\", \"OpenMenu\", function(this, menuName, userData)",
            "native subscription after author load");
        src=CyberOnce(src,"    Observe(\"DriveEvents\", \"OnUpdate\", function(this)",
            "    Observe(\"DriveEvents\", \"OnUpdate\", function(this)\n"+
            "        if __gcetStuntsLocationSleeping() then return end",
            "vehicle event work");
        // First NEAR callback seeds vehicle airborne state. Do not invent
        // a new takeoff while already airborne after a dormant FAR period.
        src=CyberRegexOnce(src,
            @"(?m)^([ \t]*local\s+isAirborne\s*=\s*vehicle:IsInAir\(\))[ \t]*$",
            "$1\n        if wasVehicleAirborne==nil then wasVehicleAirborne=isAirborne end",
            "airborne transition baseline");
        src=CyberRegexOnce(src,
            @"(?m)^([ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onUpdate[""']\s*,\s*function\s*\(\s*dt\s*\))[ \t]*$",
            "$1\n    if __gcetStuntsLocationSleeping() then return end",
            "distant frame event");
        // Phase 1+2 cheap-guard ordering belongs in the existing author-owned
        // generation rule, after generic provider rewrites and before the
        // external native worker is produced in this same composed pass.
        // Synthetic author variants without a time-dilation path need no reorder.
        if(src.Contains("oldTdValue",StringComparison.Ordinal))
        {
            src=CyberRegexOnce(src,
                @"(?m)^[ \t]*local requests[ \t]*=[ \t]*(?<requests>(?:__gcetGetSystemRequestsHandler|Game\.GetSystemRequestsHandler)\(\))[ \t]*\n" +
                @"^[ \t]*local timeSystem[ \t]*=[ \t]*(?<time>(?:__gcetGetTimeSystem|Game\.GetTimeSystem)\(\))[ \t]*\n" +
                @"(?<dilation>^[ \t]*if not oldTdValue and timeSystem and timeSystem:GetActiveTimeDilation\(\) then[ \t]*\n" +
                @"^[ \t]*oldTdValue = timeSystem:GetActiveTimeDilation\(\)[ \t]*\n^[ \t]*end[ \t]*\n)" +
                @"^[ \t]*if requests:IsPreGame\(\) or requests:IsGamePaused\(\) then[ \t]*\n" +
                @"^[ \t]*return[ \t]*\n^[ \t]*end[ \t]*\n\s*" +
                @"^[ \t]*local player[ \t]*=[ \t]*(?<player>(?:__gcetGetPlayer|Game\.GetPlayer)\(\))[ \t]*\n" +
                @"^[ \t]*if not settings.enableMod or not playerPos or not player then return end[ \t]*\n\s*" +
                @"^[ \t]*local vehicle[ \t]*=[ \t]*player.mountedVehicle[ \t]*\n" +
                @"^[ \t]*if not vehicle then[ \t]*\n^[ \t]*wasVehicleAirborne = false[ \t]*\n" +
                @"^[ \t]*return[ \t]*\n^[ \t]*end\b",
                "    if not settings.enableMod then return end\n" +
                "    local player = ${player}\n" +
                "    if not player then return end\n" +
                "    local vehicle = player.mountedVehicle\n" +
                "    if not vehicle then\n" +
                "        wasVehicleAirborne = false\n" +
                "        return\n" +
                "    end\n" +
                "    local requests = ${requests}\n" +
                "    if requests:IsPreGame() or requests:IsGamePaused() then return end\n" +
                "    local timeSystem = ${time}\n" +
                "${dilation}" +
                "    if not playerPos then return end",
                "frame cheap guards before native system getters");
        }
        context.Write(file,crlf?src.Replace("\n","\r\n",StringComparison.Ordinal):src);
        ApplyCyberStuntsNativeWorker(context);
        return SemanticInjectionResult.Success(
            "Compiled fixed launch points gate dormant vehicle/frame Lua work on known native FAR. "+
            "Active stunt, dynamic jumps, scoring and CET overlay remain author-owned; "+
            "native absent/UNKNOWN retains original behavior.");
    }
}
