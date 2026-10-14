using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplySongsDeck(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "Override(\"PlayerPuppet\", \"OnAction\"",
            "StopDeviceControl",
            "VisionHold",
            "IconicCyberware",
            "BlackwallUpload.Execute");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?m)^(?<indent>\s*)Override\s*\(\s*[""']PlayerPuppet[""']\s*,\s*[""']OnAction[""']\s*,\s*function\s*\(\s*this\s*,\s*action\s*,\s*consumer\s*,\s*wrappedMethod\s*\)\s*\r?\n\s*local\s+actionName\s*=\s*Game\.NameToString\s*\(\s*ListenerAction\.GetName\s*\(\s*action\s*\)\s*\)\s*\r?\n\s*local\s+actionType\s*=\s*ListenerAction\.GetType\s*\(\s*action\s*\)\s*$",
            "${indent}local __gcetSongsDeckActions = {\n" +
            "${indent}  StopDeviceControl = true,\n" +
            "${indent}  VisionHold = true,\n" +
            "${indent}  IconicCyberware = true,\n" +
            "${indent}  MeleeBlock = true,\n" +
            "${indent}  RangedAttack = true,\n" +
            "${indent}  MeleeAttack = true\n" +
            "${indent}}\n" +
            "${indent}Override(\"PlayerPuppet\", \"OnAction\", function(this, action, consumer, wrappedMethod)\n" +
            "${indent}  local actionName = Game.NameToString(ListenerAction.GetName(action))\n" +
            "${indent}  if not actionLog and not __gcetSongsDeckActions[actionName] then\n" +
            "${indent}    return wrappedMethod(action, consumer)\n" +
            "${indent}  end\n" +
            "${indent}  local actionType = ListenerAction.GetType(action)",
            "SongsDeck exact override prefilter");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Kept SongsDeck's PlayerPuppet Override and consume/return semantics intact, but returns directly to wrappedMethod for every action outside the six source-proven names; actionLog still preserves all-action diagnostics when explicitly enabled.");
    }

}
