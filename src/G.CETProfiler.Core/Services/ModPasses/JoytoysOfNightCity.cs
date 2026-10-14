using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyJoytoysOfNightCity(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "writeReadyState(quests)",
            "processLocationRequest(quests)",
            "processJobRequests(quests)",
            "if Joytoys.NPCScenes then Joytoys.NPCScenes.update(dt) end",
            "if Joytoys.SceneDirector then Joytoys.SceneDirector.update(dt) end",
            "if Shift.stage == 0 then",
            "ensureOffer()",
            "Joytoys._nifIdleTimer >= 2.0");

        var scenes = context.FindFile(
            "npc_scenes.lua",
            "function controller.update(deltaTime)",
            "controller.activeRequest",
            "isRunning()");

        var director = context.FindFile(
            "scene_director.lua",
            "local function statusSnapshot()",
            "local runtimeStatus = runtime and runtime.status()",
            "STATE_CODE[director.phase]",
            "OUTCOME_CODE[",
            "function director.status()");

        // Upgrade both the unmodified author source and a previously applied
        // G-CET v1 bridge split. Do not slow or modify active scene updates.
        var alreadyBridgeSplit = init.Text.Contains(
            "if Joytoys._bridgePollTimer >= 0.10 then",
            StringComparison.Ordinal);


        var initText = init.Text;
        if (!alreadyBridgeSplit)
        {
            initText = ReplaceOnce(
            initText,
            "  _sceneFactTimer = 0.0,\n}",
            "  _sceneFactTimer = 0.0,\n" +
            "  _bridgePollTimer = 0.10,\n" +
            "  _offerPollTimer = 0.50,\n}",
            "Joytoys bridge/offer sentinel state");

        var oldUpdate =
            "  writeReadyState(quests)\n" +
            "  processLocationRequest(quests)\n" +
            "  processJobRequests(quests)\n" +
            "  updateImmersiveProof(dt)\n" +
            "  if Joytoys.NPCScenes then Joytoys.NPCScenes.update(dt) end\n" +
            "  if Joytoys.SceneDirector then Joytoys.SceneDirector.update(dt) end\n" +
            "  Joytoys._sceneFactTimer = Joytoys._sceneFactTimer + (tonumber(dt) or 0.0)\n" +
            "  if Joytoys.SceneDirector and Joytoys._sceneFactTimer >= 0.5 then\n" +
            "    Joytoys._sceneFactTimer = 0.0\n" +
            "    writeSceneDirectorFacts(Joytoys.SceneDirector.status())\n" +
            "  end\n\n" +
            "  if Shift.stage == 0 then\n" +
            "    ensureOffer()\n" +
            "    Joytoys._nifIdleTimer = Joytoys._nifIdleTimer + (tonumber(dt) or 0.0)\n" +
            "    if Joytoys._nifIdleTimer >= 2.0 then\n" +
            "      Joytoys._nifIdleTimer = 0.0\n" +
            "      setNifProject(false)\n" +
            "    end\n" +
            "  end\n";

        var newUpdate =
            "  local __gcetJncDt = math.max(0.0, tonumber(dt) or 0.0)\n" +
            "  Joytoys._bridgePollTimer = Joytoys._bridgePollTimer + __gcetJncDt\n" +
            "  if Joytoys._bridgePollTimer >= 0.10 then\n" +
            "    Joytoys._bridgePollTimer = 0.0\n" +
            "    writeReadyState(quests)\n" +
            "    processLocationRequest(quests)\n" +
            "    processJobRequests(quests)\n" +
            "  end\n\n" +
            "  updateImmersiveProof(dt)\n" +
            "  if Joytoys.NPCScenes then Joytoys.NPCScenes.update(dt) end\n" +
            "  if Joytoys.SceneDirector then Joytoys.SceneDirector.update(dt) end\n" +
            "  Joytoys._sceneFactTimer = Joytoys._sceneFactTimer + __gcetJncDt\n" +
            "  if Joytoys.SceneDirector and Joytoys._sceneFactTimer >= 0.5 then\n" +
            "    Joytoys._sceneFactTimer = 0.0\n" +
            "    writeSceneDirectorFacts(Joytoys.SceneDirector.status())\n" +
            "  end\n\n" +
            "  if Shift.stage == 0 then\n" +
            "    Joytoys._offerPollTimer = Joytoys._offerPollTimer + __gcetJncDt\n" +
            "    if Joytoys._offerPollTimer >= 0.50 then\n" +
            "      Joytoys._offerPollTimer = 0.0\n" +
            "      ensureOffer()\n" +
            "    end\n" +
            "    Joytoys._nifIdleTimer = Joytoys._nifIdleTimer + __gcetJncDt\n" +
            "    if Joytoys._nifIdleTimer >= 2.0 then\n" +
            "      Joytoys._nifIdleTimer = 0.0\n" +
            "      setNifProject(false)\n" +
            "    end\n" +
            "  else\n" +
            "    Joytoys._offerPollTimer = 0.50\n" +
            "  end\n";

        initText = ReplaceOnce(
            initText,
            oldUpdate,
            newUpdate,
            "Joytoys bridge/offer active split");
        }

        initText = ReplaceOnce(
            initText,
            "writeSceneDirectorFacts(Joytoys.SceneDirector.status())",
            "writeSceneDirectorFacts(Joytoys.SceneDirector.factStatus())",
            "Joytoys idle scene-fact mirror avoids full runtime status");
        context.Write(init, initText);

        var sceneText = alreadyBridgeSplit ? scenes.Text : ReplaceOnce(
            scenes.Text,
            "  function controller.update(deltaTime)\n" +
            "    local api = controller.provider and controller.provider.api or nil\n",
            "  function controller.update(deltaTime)\n" +
            "    if not controller.activeRequest then return end\n" +
            "    local api = controller.provider and controller.provider.api or nil\n",
            "Joytoys inactive NPC scene dormancy");
        if (alreadyBridgeSplit)
        {
            if (!sceneText.Contains(
                "if not controller.activeRequest then return end",
                StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Joytoys v1 NPC scene dormancy missing; refusing partial upgrade.");
            // Keep all three files on the v2 marker so already-satisfied and
            // partial-state checks describe the complete composed semantic.
            sceneText += "\n-- G-CET v2 preserves existing active-request NPC scene gate\n";
        }
        context.Write(scenes, sceneText);

        var directorText = ReplaceOnce(
            director.Text,
            "  function director.status()\n" +
            "    return statusSnapshot()\n" +
            "  end\n",
            "  function director.status()\n" +
            "    return statusSnapshot()\n" +
            "  end\n" +
            "\n" +
            "  -- G-CET: only the seven scene facts are needed by idle mirroring.\n" +
            "  -- Full diagnostics and all active sessions keep statusSnapshot().\n" +
            "  function director.factStatus()\n" +
            "    if director.session then return statusSnapshot() end\n" +
            "    local outcome = director.lastSession and director.lastSession.outcome or 'none'\n" +
            "    return {\n" +
            "      stateCode = STATE_CODE[director.phase] or -1,\n" +
            "      sessionSequence = 0, definitionId = 0, profileId = 0,\n" +
            "      locationId = 0, cameraIndex = 0,\n" +
            "      outcomeCode = OUTCOME_CODE[outcome] or 0,\n" +
            "    }\n" +
            "  end\n",
            "Joytoys idle fact-only status without FreeFly/player discovery");
        context.Write(director, directorText);

        return SemanticInjectionResult.Success(
            "Preserves the v1 bridge sentinel, real-time scene paths and all author cadences. The 0.5 s scene-fact mirror now uses exact seven-field idle facts without invoking runtime.status/FreeFly discovery; active scenes still call full statusSnapshot(). Upgrades original or G-CET v1 source.");
    }


}
