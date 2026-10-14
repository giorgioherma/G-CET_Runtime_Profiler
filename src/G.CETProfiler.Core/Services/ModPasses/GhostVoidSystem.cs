using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyGhostVoidSystem(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "GVS.GhostVoid.GVSStateSystem",
            "local function addVoidEnergy",
            "local function addStability",
            "corruptionGlitchTimer",
            "corruptionDrainTimer",
            "voidEnergyRegenPerSecond",
            "stabilityRegenPerSecond");

        var text = ReplaceOnce(
            file.Text,
            "local function addVoidEnergy(amount)\n  local state = getState()",
            "local function addVoidEnergy(amount, state)\n  state = state or getState()",
            "Ghost Void optional state reuse for void energy");

        text = ReplaceOnce(
            text,
            "local function addStability(amount)\n  local state = getState()",
            "local function addStability(amount, state)\n  state = state or getState()",
            "Ghost Void optional state reuse for stability");

        text = ReplaceOnce(
            text,
            "  local corruptionState = getState()\n",
            "  local __gcetGvsState = getState()\n  local corruptionState = __gcetGvsState\n",
            "Ghost Void frame state acquisition");

        text = ReplaceOnce(
            text,
            "  local drainState = getState()\n",
            "  local drainState = __gcetGvsState\n",
            "Ghost Void drain state reuse");

        text = ReplaceOnce(
            text,
            "    addVoidEnergy(gvs.voidEnergyRegenPerSecond * deltaTime)\n",
            "    addVoidEnergy(gvs.voidEnergyRegenPerSecond * deltaTime, __gcetGvsState)\n",
            "Ghost Void energy regeneration state reuse");

        text = ReplaceOnce(
            text,
            "  local stabilityState = getState()\n",
            "  local stabilityState = __gcetGvsState\n",
            "Ghost Void stability state reuse");

        text = ReplaceOnce(
            text,
            "      addStability(\n" +
            "        gvs.stabilityRegenPerSecond * deltaTime\n" +
            "      )",
            "      addStability(\n" +
            "        gvs.stabilityRegenPerSecond * deltaTime,\n" +
            "        __gcetGvsState\n" +
            "      )",
            "Ghost Void stability regeneration state reuse");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Resolved Ghost Void's ScriptableSystem once per update and reused that authoritative object for corruption/drain/regen work. Timers, delta-time amounts, HUD and visual-effect cadence are unchanged.");
    }

}
