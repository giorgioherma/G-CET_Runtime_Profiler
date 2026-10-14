using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyShift(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "Modules/Lifecycle/UpdateLoop.lua",
            "VehicleState.update(delta)",
            "WeaponState.update(delta)",
            "CameraEngine.update(delta)");

        var anchor = "local UpdateLoop = {}\n";
        var text = ReplaceOnce(
            file.Text,
            anchor,
            anchor +
            "local __gcetShiftStateElapsed = 0.0\n" +
            "local __gcetShiftStateInterval = 0.05\n",
            "Shift state cadence declarations");

        var stateUpdates =
            "    VehicleState.update(delta)\n" +
            "    WeaponState.update(delta)\n";

        var stateGated =
            "    __gcetShiftStateElapsed = __gcetShiftStateElapsed + delta\n" +
            "    if __gcetShiftStateElapsed >= __gcetShiftStateInterval then\n" +
            "        local __gcetStateDelta = __gcetShiftStateElapsed\n" +
            "        __gcetShiftStateElapsed = 0.0\n" +
            "        VehicleState.update(__gcetStateDelta)\n" +
            "        WeaponState.update(__gcetStateDelta)\n" +
            "    end\n";

        text = ReplaceOnce(
            text,
            stateUpdates,
            stateGated,
            "Shift vehicle/weapon state lane");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Preserved camera, shake, locomotion, Cron and speed interpolation every frame; moved vehicle/weapon state reconciliation to 20 Hz.");
    }


    // GC-source-proven, allocation-only AutoLoot optimization. This affects
    // neither native mappin polling nor author cadence. It fails closed if
    // the known candidate scan shape or pure squared-distance helper changes.
}
