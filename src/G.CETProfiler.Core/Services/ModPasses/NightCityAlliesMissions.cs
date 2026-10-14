using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyNightCityAlliesMissions(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "function NCA_Missions:SaveMissions",
            "function NCA_Missions:LoadMissions",
            "CargoHeist.Update(dt, db)",
            "BountySystem.UpdateRadar(db)",
            "NCA_MissionDB");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?m)^(?<opening>[ \t]*function\s+NCA_Missions:SaveMissions\s*\(\s*db\s*\)\s*)\r?\n",
            "${opening}\n    NCA_MissionDB = db\n",
            "NCA Missions SaveMissions cache synchronization");

        text = RegexReplaceOnce(
            text,
            @"(?m)^[ \t]*db\.bmi\s*=\s*db\.bmi\s*or\s*0\s*\r?\n(?:[ \t]*\r?\n)?[ \t]*return\s+db\s*$",
            "    db.bmi = db.bmi or 0\n    NCA_MissionDB = db\n    return db",
            "NCA Missions LoadMissions cache synchronization");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<header>[ \t]*--\s*2\.[^\r\n]*\r?\n)(?<comment>[ \t]*--[^\r\n]*\r?\n)(?<indent>[ \t]*)local\s+db\s*=\s*NCA_Missions:LoadMissions\s*\(\s*\)\s*$",
            "${header}${comment}${indent}local db = NCA_MissionDB or NCA_Missions:LoadMissions()",
            "NCA Missions per-frame mission database load");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Kept Cargo Heist, bounty and mission timers at the author's original cadence while making the already-global mission table authoritative between explicit loads/saves, removing the per-frame JSON disk round-trip.");
    }


}
