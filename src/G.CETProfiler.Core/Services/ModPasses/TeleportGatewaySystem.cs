using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyTeleportGatewaySystem(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "TeleportGatewaySystem",
            "playerPos = TGS.player:GetWorldPosition()",
            "GWDist = math.sqrt",
            "TGS.player:GetWorldPosition().x-gatewayDB[index].gwx",
            "TGS.teleportFac:Teleport",
            "TGS.showMainWindow");

        var text = ReplaceOnce(
            file.Text,
            "GWDist = math.sqrt(((TGS.player:GetWorldPosition().x-gatewayDB[index].gwx)^2)+((TGS.player:GetWorldPosition().y-gatewayDB[index].gwy)^2)+((TGS.player:GetWorldPosition().z-gatewayDB[index].gwz)^2))",
            "GWDist = math.sqrt(((playerPos.x-gatewayDB[index].gwx)^2)+((playerPos.y-gatewayDB[index].gwy)^2)+((playerPos.z-gatewayDB[index].gwz)^2))",
            "Teleport Gateway per-gateway position reuse");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<opening>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onDraw[""']\s*,\s*function\s*\(\s*\)\s*)$",
            "${opening}\n\tif not TGS.showMainWindow then return end",
            "Teleport Gateway hidden-window draw gate");

        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Preserved every-frame gateway detection and the original state machine/cooldown behavior, reused the already-read player position inside the gateway loop, and skipped ImGui work while the gateway window is hidden.");
    }

}
