using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyCombat(
        SemanticPatchContext context)
    {
        var enemy = context.FindFile(
            "Modules/EnemyBase.lua",
            "ObserveAfter(\"PlayerPuppet\", \"IsInCombat\"",
            "throttledUpdateT1",
            "throttledUpdateT4");

        var enemyOpening =
            "---@param this PlayerPuppet\n" +
            "ObserveAfter(\"PlayerPuppet\", \"IsInCombat\", function(this)\n";

        var enemyReplacement =
            "-- G-CET transition cache: IsInCombat is queried extremely often; the original\n" +
            "-- Combat body only needs to run when its combat/locomotion class changes.\n" +
            "local __gcetCombatEnemyBaseLastState = nil\n\n" +
            enemyOpening +
            "\tlocal __gcetInCombat = this.inCombat == true\n" +
            "\tlocal __gcetMovingHorizontally = __gcetInCombat and this:IsMovingHorizontally() == true or false\n" +
            "\tlocal __gcetMovingVertically = __gcetInCombat and this:IsMovingVertically() == true or false\n" +
            "\tlocal __gcetState = (__gcetInCombat and 4 or 0) + (__gcetMovingHorizontally and 1 or 0) + (__gcetMovingVertically and 2 or 0)\n" +
            "\tif __gcetState == __gcetCombatEnemyBaseLastState then return end\n" +
            "\t__gcetCombatEnemyBaseLastState = __gcetState\n";

        var enemyText = ReplaceOnce(
            enemy.Text,
            enemyOpening,
            enemyReplacement,
            "Combat EnemyBase IsInCombat observer");
        context.Write(enemy, enemyText);

        var immersive = context.FindFile(
            "Modules/Immersive Effect.lua",
            "ObserveAfter(\"PlayerPuppet\", \"IsInCombat\"",
            "DisableCameraShakeState",
            "TweakDB:SetFlat");

        var immersiveOpening =
            "---@param this PlayerPuppet\n" +
            "ObserveAfter(\"PlayerPuppet\", \"IsInCombat\",function(this)\n";

        var immersiveReplacement =
            "-- G-CET transition cache: preserve the author's body, but execute it only\n" +
            "-- when locomotion or the two settings that affect its output change.\n" +
            "local __gcetCombatImmersiveLastState = nil\n\n" +
            immersiveOpening +
            "    local __gcetMovingHorizontally = this:IsMovingHorizontally() == true\n" +
            "    local __gcetMovingVertically = this:IsMovingVertically() == true\n" +
            "    local __gcetState = (__gcetMovingHorizontally and 1 or 0) + (__gcetMovingVertically and 2 or 0)\n" +
            "        + (Setting.DisableStunState and 4 or 0) + (Setting.DisableCameraShakeState and 8 or 0)\n" +
            "    if __gcetState == __gcetCombatImmersiveLastState then return end\n" +
            "    __gcetCombatImmersiveLastState = __gcetState\n";

        var immersiveText = ReplaceOnce(
            immersive.Text,
            immersiveOpening,
            immersiveReplacement,
            "Combat Immersive Effect IsInCombat observer");
        context.Write(immersive, immersiveText);

        return SemanticInjectionResult.Success(
            "Injected per-module transition guards around the two hot Combat IsInCombat observers; original Combat work remains unchanged behind the guards.");
    }

}
