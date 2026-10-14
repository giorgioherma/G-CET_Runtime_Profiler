using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyDroneCompanionsRevamp(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "DroneLogic/Drone AI - Mech.lua",
            "Override('TweakAIActionAbstract', 'Update'",
            "MinotaurMech.AimAttackHMG",
            "MinotaurMech.RotateToTargetNoLimit",
            "DroneOctantActions.ShootDefault",
            "DroneBombusActions.FollowTargetFast",
            "TagsContains(CName.new(\"Robot\"))");

        var text = ReplaceOnce(
            file.Text,
            "    local mechcount, mechcount2, octantcount, bombuscount = 0, 0, 0, 0\n" +
            "    Override('TweakAIActionAbstract', 'Update', function(self, context, wrappedMethod)\n" +
            "        local owner = ScriptExecutionContext.GetOwner(context)\n",
            "    local mechcount, mechcount2, octantcount, bombuscount = 0, 0, 0, 0\n" +
            "    local __gcetDcoAimHmg = TweakDBID.new(\"MinotaurMech.AimAttackHMG\")\n" +
            "    local __gcetDcoRotate = TweakDBID.new(\"MinotaurMech.RotateToTargetNoLimit\")\n" +
            "    local __gcetDcoOctantShoot = TweakDBID.new(\"DroneOctantActions.ShootDefault\")\n" +
            "    local __gcetDcoBombusFollow = TweakDBID.new(\"DroneBombusActions.FollowTargetFast\")\n" +
            "    Override('TweakAIActionAbstract', 'Update', function(self, context, wrappedMethod)\n" +
            "        local recordID = (self.actionRecord and self.actionRecord:GetID()) or TweakDBID.new(\"\")\n" +
            "        if recordID ~= __gcetDcoAimHmg and recordID ~= __gcetDcoRotate\n" +
            "           and recordID ~= __gcetDcoOctantShoot and recordID ~= __gcetDcoBombusFollow then\n" +
            "            return wrappedMethod(context)\n" +
            "        end\n" +
            "        local owner = ScriptExecutionContext.GetOwner(context)\n",
            "Drone Companions exact AI action-record prefilter");

        text = ReplaceOnce(
            text,
            "            local recordID = (self.actionRecord and self.actionRecord:GetID()) or TweakDBID.new(\"\")\n" +
            "            if recordID == TweakDBID.new(\"MinotaurMech.AimAttackHMG\") then",
            "            if recordID == __gcetDcoAimHmg then",
            "Drone Companions cached first record ID");

        text = ReplaceOnce(
            text,
            "            elseif recordID == TweakDBID.new(\"MinotaurMech.RotateToTargetNoLimit\") then",
            "            elseif recordID == __gcetDcoRotate then",
            "Drone Companions cached rotate record ID");
        text = ReplaceOnce(
            text,
            "            elseif recordID == TweakDBID.new(\"DroneOctantActions.ShootDefault\") then",
            "            elseif recordID == __gcetDcoOctantShoot then",
            "Drone Companions cached octant record ID");
        text = ReplaceOnce(
            text,
            "            elseif recordID == TweakDBID.new(\"DroneBombusActions.FollowTargetFast\") then",
            "            elseif recordID == __gcetDcoBombusFollow then",
            "Drone Companions cached bombus record ID");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Prefiltered the global TweakAIActionAbstract::Update override by the only four action records it can modify, before owner/tag/player work. Robot/FistFight behavior for those records remains unchanged.");
    }

}
