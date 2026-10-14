using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyDiscardAmmoOnReload(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "local swapActions",
            "local nonSwapActions",
            "Observe('PlayerPuppet','OnAction'",
            "ReloadSystem.weaponSwap");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?ms)^(?<indent>[ \t]*)Observe\s*\(\s*['""]PlayerPuppet['""]\s*,\s*['""]OnAction['""]\s*,\s*function\s*\(\s*self\s*,\s*action\s*\)\s*\r?\n\s*local\s+actionName\s*=\s*Game\.NameToString\s*\(\s*action:GetName\s*\(\s*\)\s*\)\s*\r?\n\s*if\s*\(\s*ReloadSystem\.weaponSwap\s*\)\s*then\s*\r?\n\s*if\s*\(\s*Utility\.filter\s*\(\s*nonSwapActions\s*,\s*actionName\s*\)\s*\)\s*then\s*\r?\n\s*ReloadSystem\.weaponSwap\s*=\s*false\s*\r?\n\s*end\s*\r?\n\s*elseif\s*\(\s*Utility\.filter\s*\(\s*swapActions\s*,\s*actionName\s*\)\s*\)\s*then\s*\r?\n\s*ReloadSystem\.weaponSwap\s*=\s*true\s*\r?\n\s*end\s*\r?\n\s*end\s*\)\s*$",
            "${indent}local function __gcetDiscardAmmoOnAction(self, action, __gcetConsumer, __gcetRoutedName)\n" +
            "${indent}    local actionName = __gcetRoutedName or Game.NameToString(action:GetName())\n" +
            "${indent}    if ReloadSystem.weaponSwap then\n" +
            "${indent}        if Utility.filter(nonSwapActions, actionName) then\n" +
            "${indent}            ReloadSystem.weaponSwap = false\n" +
            "${indent}        end\n" +
            "${indent}    elseif Utility.filter(swapActions, actionName) then\n" +
            "${indent}        ReloadSystem.weaponSwap = true\n" +
            "${indent}    end\n" +
            "${indent}end\n\n" +
            "${indent}local __gcetDiscardAmmoRouted = false\n" +
            "${indent}local __gcetDiscardAmmoHandles = {}\n" +
            "${indent}local __gcetDiscardAmmoOk, __gcetDiscardAmmoEngine = pcall(GetMod, \"0-Engine\")\n" +
            "${indent}local __gcetDiscardAmmoApi = __gcetDiscardAmmoEngine\n" +
            "${indent}if __gcetDiscardAmmoOk and type(__gcetDiscardAmmoEngine) == \"table\" and type(__gcetDiscardAmmoEngine.GCET) == \"table\" then __gcetDiscardAmmoApi = __gcetDiscardAmmoEngine.GCET end\n" +
            "${indent}if __gcetDiscardAmmoOk and type(__gcetDiscardAmmoApi) == \"table\" and type(__gcetDiscardAmmoApi.SubscribeAction) == \"function\" then\n" +
            "${indent}    __gcetDiscardAmmoRouted = pcall(function()\n" +
            "${indent}        __gcetDiscardAmmoHandles[#__gcetDiscardAmmoHandles + 1] = __gcetDiscardAmmoApi.SubscribeAction({\n" +
            "${indent}            id = \"G-CET.Semantic.DiscardAmmoOnReload\",\n" +
            "${indent}            actions = { \"PreviousWeapon\", \"NextWeapon\", \"WeaponSlot1\", \"WeaponSlot2\", \"WeaponSlot3\", \"WeaponWheel\", \"RangedAttack\", \"MeleeAttack\" },\n" +
            "${indent}            decodeType = false\n" +
            "${indent}        }, __gcetDiscardAmmoOnAction, \"DiscardAmmoOnReload\")\n" +
            "${indent}    end)\n" +
            "${indent}    if not __gcetDiscardAmmoRouted then\n" +
            "${indent}        for _, __gcetHandle in ipairs(__gcetDiscardAmmoHandles) do\n" +
            "${indent}            if __gcetHandle and type(__gcetHandle.unsubscribe) == \"function\" then pcall(__gcetHandle.unsubscribe) end\n" +
            "${indent}        end\n" +
            "${indent}    end\n" +
            "${indent}end\n" +
            "${indent}if not __gcetDiscardAmmoRouted then Observe('PlayerPuppet','OnAction', function(self, action) __gcetDiscardAmmoOnAction(self, action, nil, nil) end) end",
            "Discard Ammo exact action routing");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Routed only the eight source-proven weapon-swap/reset actions through 0-Engine while preserving the original ReloadSystem state transitions and direct Observe fallback.");
    }

}
