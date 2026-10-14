using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyDriveAerialVehicle(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "DAV.core_obj.av_obj.engine_obj:Update(delta)",
            "Def.Situation.Idle",
            "Cron.Update(delta)");

        var initText = RegexReplaceOnce(
            init.Text,
            @"(?m)^(?<indent>\s*)DAV\.core_obj\.av_obj\.engine_obj:Update\(delta\)\s*$",
            "${indent}local __gcetEvent = DAV.core_obj.event_obj\n" +
            "${indent}local __gcetSituation = __gcetEvent and __gcetEvent.current_situation or Def.Situation.Idle\n" +
            "${indent}if __gcetSituation ~= Def.Situation.Idle and __gcetSituation ~= Def.Situation.Normal then\n" +
            "${indent}\tDAV.core_obj.av_obj.engine_obj:Update(delta)\n" +
            "${indent}end",
            "DriveAerialVehicle realtime physics state gate");
        context.Write(init, initText);

        var core = context.FindFile(
            "Modules/core.lua",
            "Cron.Every(DAV.time_resolution",
            "self.event_obj:CheckAllEvents()",
            "self:GetActions()",
            "Def.Situation.Normal");

        var pattern =
            @"Cron\.Every\(DAV\.time_resolution\s*,\s*function\s*\(\s*\)\s*" +
            @"self\.event_obj:CheckAllEvents\(\)\s*" +
            @"self:GetActions\(\)\s*" +
            @"end\s*\)";

        var replacement =
            "local __gcetIdleEventTick = 0\n" +
            "    Cron.Every(DAV.time_resolution, function()\n" +
            "        local __gcetSituation = self.event_obj.current_situation\n" +
            "        local __gcetActiveVehicleState = __gcetSituation ~= Def.Situation.Idle and __gcetSituation ~= Def.Situation.Normal\n" +
            "        if __gcetActiveVehicleState then\n" +
            "            __gcetIdleEventTick = 0\n" +
            "            self.event_obj:CheckAllEvents()\n" +
            "        elseif __gcetSituation == Def.Situation.Normal then\n" +
            "            __gcetIdleEventTick = __gcetIdleEventTick + 1\n" +
            "            if __gcetIdleEventTick >= 8 then\n" +
            "                __gcetIdleEventTick = 0\n" +
            "                self.event_obj:CheckAllEvents()\n" +
            "            end\n" +
            "        end\n" +
            "        if __gcetActiveVehicleState or not self.queue_obj:IsEmpty() then\n" +
            "            self:GetActions()\n" +
            "        end\n" +
            "    end)";

        var coreText = RegexReplaceOnce(
            core.Text,
            pattern,
            replacement,
            "DriveAerialVehicle active/normal event cadence");
        context.Write(core, coreText);

        return SemanticInjectionResult.Success(
            "Kept flight/landing/waiting/takeoff physics and event checks realtime, stopped physics in Normal/Idle, slowed Normal reconciliation, and preserved immediate queued actions.");
    }

}
