using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyGiveMeEverything(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "tabs/teleport.lua",
            "BaseMappinBaseController",
            "UpdateRootState",
            "CustomPositionVariant",
            "GetWorldPosition");

        var pattern =
            @"(?m)^(?<opening>\s*Observe\s*\(\s*[""']BaseMappinBaseController[""']\s*,\s*[""']UpdateRootState[""']\s*,\s*function\s*\(\s*self\s*\)\s*)$";

        var replacement =
            "${opening}\n" +
            "        -- G-CET semantic ownership prefilter: this observer is global but\n" +
            "        -- GiveMeEverything only owns the custom waypoint variant.\n" +
            "        local __gcetMappin = self:GetMappin()\n" +
            "        if not __gcetMappin or __gcetMappin:GetVariant() ~= gamedataMappinVariant.CustomPositionVariant then return end";

        var text = RegexReplaceOnce(
            file.Text,
            pattern,
            replacement,
            "GiveMeEverything custom waypoint mappin prefilter");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Rejected unrelated BaseMappin UpdateRootState callbacks before GiveMeEverything performs custom-waypoint world/native work.");
    }

}
