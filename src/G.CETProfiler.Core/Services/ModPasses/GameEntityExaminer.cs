using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyGameEntityExaminer(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "settings.autoFreeze",
            "GetComponentClosestToCrosshair",
            "Cron.Update(delta)");

        var opening = "__gcetRegisterEvent_125('onUpdate', function(delta)\n";
        if (!file.Text.Contains(opening, StringComparison.Ordinal))
            opening = FindOnUpdateOpening(file.Text, "delta");

        var text = ReplaceOnce(
            file.Text,
            opening,
            "local __gcetExaminerScanElapsed = 0.0\n\n" + opening,
            "GameEntityExaminerTool onUpdate opening");

        var boundary =
            "    if rcEnt and not rcEnt:IsVehicleRemoteControlled() then\n" +
            "        rcEnt = nil\n" +
            "    end\n" +
            "    if settings.autoFreeze == true then\n";

        var gatedBoundary =
            "    if rcEnt and not rcEnt:IsVehicleRemoteControlled() then\n" +
            "        rcEnt = nil\n" +
            "    end\n" +
            "    local __gcetScanActive = settings.autoFreeze == true\n" +
            "        or settings.buttonsOn == true\n" +
            "        or settings.highlightEnts == true\n" +
            "        or cetopen == true\n" +
            "    -- Entity-target hotkeys consume the cached ent/entID even with the\n" +
            "    -- overlay/scanner hidden, so the dormant lane must keep a low-rate\n" +
            "    -- target refresh instead of going fully asleep.\n" +
            "    __gcetExaminerScanElapsed = __gcetExaminerScanElapsed + delta\n" +
            "    local __gcetScanInterval = __gcetScanActive and (1 / 30) or 0.20\n" +
            "    if __gcetExaminerScanElapsed < __gcetScanInterval then return end\n" +
            "    delta = __gcetExaminerScanElapsed\n" +
            "    __gcetExaminerScanElapsed = 0.0\n" +
            "    if settings.autoFreeze == true then\n";

        text = ReplaceOnce(
            text,
            boundary,
            gatedBoundary,
            "GameEntityExaminerTool active scan boundary");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Kept Cron and remote-control vehicle following frame-responsive; scanner/entity examination reconciles at 30 Hz while active and retains a 5 Hz dormant target-refresh lane for hotkey correctness.");
    }


}
