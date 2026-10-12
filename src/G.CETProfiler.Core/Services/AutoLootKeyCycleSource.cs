using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// One upgraded AutoLoot semantic, not an additional competing onUpdate rule.
// Source cases: untouched author 3.11.x -> world-pin scalars + early loot radius;
// installed prior world-pin-only v1 -> retain its exact code, add radius only.
// No change to original key cadence, native mappin query, quest protections or transfers.
internal static class AutoLootKeyCycleSource
{
    internal static SemanticInjectionResult Apply(SemanticPatchContext context)
    {
        var file = context.FindFile(
            "modules/logic.lua",
            "function logic.lootInRange(",
            "function isWorldMapPinOfTypeInRange(",
            "local function vectorDistanceSquared(src, target)",
            "mappinSystem:GetMappinEntries(gamemappinsMappinTargetTypeWorld)",
            "mappinSystem:GetMappins(gamemappinsMappinTargetTypeWorld)");

        var worldIsOptimized =
            file.Text.Contains("local firstMappin, nearestMappin", StringComparison.Ordinal) &&
            file.Text.Contains("if matchingMappins == 1 then return firstMappin end", StringComparison.Ordinal) &&
            file.Text.Contains("lowestDistanceIndex = nearestIndex", StringComparison.Ordinal) &&
            file.Text.Contains("return nearestMappin", StringComparison.Ordinal);

        var worldIsOriginal =
            file.Text.Contains("local closeMappins = {}", StringComparison.Ordinal) &&
            file.Text.Contains("if takeIt then tableInsert(closeMappins, mappin) end", StringComparison.Ordinal) &&
            file.Text.Contains("return closeMappins[lowestDistanceIndex]", StringComparison.Ordinal);

        if (worldIsOriginal == worldIsOptimized)
            throw new InvalidOperationException(
                "AutoLoot world-pin scan is unknown/partial; cannot safely generate the combined v2 semantic.");

        if (worldIsOriginal)
        {
            // Reuse the proven v1 author-source transform, do not hand-code an
            // approximate second implementation. Context.Write stages its result.
            SemanticInjectors.ApplyAutoLootWorldPinAllocationScan(context);
            file = context.FindFile("modules/logic.lua",
                "function logic.lootInRange(",
                "if matchingMappins == 1 then return firstMappin end",
                "function isWorldMapPinOfTypeInRange(");
        }

        var text=file.Text;
        var start=text.IndexOf("function logic.lootInRange(",StringComparison.Ordinal);
        var next=start<0?-1:text.IndexOf("function lootNpc(",start,StringComparison.Ordinal);
        if(start<0 || next<0 ||
           text.IndexOf("function logic.lootInRange(",start+1,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("AutoLoot key-triggered loot cycle boundaries changed.");

        var body=text[start..next];
        const string originalCheck="if isNpcSafeToLoot(gameObj) then";
        const string newCheck="if isNpcSafeToLoot(gameObj) and (not lootInRange or vectorDistanceSquared(playerPos, gameObj:GetWorldPosition()) <= rangeSquared) then";

        // The original function must enumerate inventory BEFORE doing its
        // own exact range check. This is the source-proven inefficiency.
        var inventory=body.IndexOf("transactionSystem:GetItemList(gameObj)",StringComparison.Ordinal);
        var ownerInventory=body.IndexOf("transactionSystem:GetItemList(gameObjOwner)",StringComparison.Ordinal);
        var originalRange=body.IndexOf("if lootInRange then",StringComparison.Ordinal);
        var laterDistance=body.IndexOf(
            "takeIt = vectorDistanceSquared(playerPos, gameObjPos) <= rangeSquared",
            StringComparison.Ordinal);
        var laterFrustum=body.IndexOf(
            "cameraSystem:IsInCameraFrustum(gameObj, 0.5, 0.2)",
            StringComparison.Ordinal);
        var laterVisibility=body.IndexOf(
            "isObjectVisibleToPlayer(gameObj, gameObjPos, player, playerPos, true)",
            StringComparison.Ordinal);
        var transfer=body.IndexOf("lootObjectItems(gameObj, forceShard",StringComparison.Ordinal);
        var originalCount=Regex.Matches(body,Regex.Escape(originalCheck)).Count;
        if(originalCount!=1 || inventory<=0 || ownerInventory<=inventory ||
           originalRange<=ownerInventory || laterDistance<=originalRange ||
           laterFrustum<=laterDistance || laterVisibility<=laterFrustum ||
           transfer<=laterVisibility || body.Contains(newCheck,StringComparison.Ordinal))
            throw new InvalidOperationException(
                "AutoLoot input-to-inventory-to-range flow changed; early-cull source proof failed.");

        // Change only the predicate: all original callbacks, item ownership,
        // quest safeguards, frustum, LoS and range checks stay in place.
        body=body.Replace(originalCheck,newCheck,StringComparison.Ordinal);
        text=text[..start]+body+text[next..];
        context.Write(file,text);
        return SemanticInjectionResult.Success(
            "Eliminated distant-object inventory and owner-inventory enumeration during " +
            "key-held 0.33s loot cycles. Retained all original distance/frustum/visibility " +
            "and quest safeguards, input routing, timing and world-pin scalar selection. " +
            (worldIsOriginal ? "Generated author world-pin optimization. " :
             "Preserved existing v1 optimized world-pin scan. "));
    }
}
