using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    // ELM world interactions are delivered by its real Show/Hide exports and
    // UI/native observers. Only skip onUpdate service work with NO pending
    // scene, scheduled job, custom hub or active earned reward/timer.
    private static SemanticInjectionResult ApplyElmIdleGc(SemanticPatchContext context)
    {
        var init=context.FindFile("init.lua",
            "SceneInteraction.Update()", "updateDarkFutureInfoPanelFollow",
            "EarnXP.Tick(delta)", "ToiletEffect.Tick(delta)");
        var scene=context.FindFile("elm_scene_interaction.lua",
            "function SceneInteraction.Show(choiceUniqueID)",
            "function SceneInteraction.Hide()", "function SceneInteraction.Update()");
        var xp=context.FindFile("elm_earnxp.lua",
            "function EarnXP.Start(rawConfig)", "function EarnXP.Stop()",
            "function EarnXP.Tick(delta)", "return EarnXP");
        var s=init.Text;
        var ui=scene.Text;
        var e=xp.Text;
        const string marker="G-CET semantic:elm-immersiveway-idle-gc-v1";
        const string uiName="function SceneInteraction.GCET_IsUiActive()";
        const string xpName="function EarnXP.GCET_IsActive()";
        var mainDone=s.Contains(marker,StringComparison.Ordinal);
        var uiDone=ui.Contains(uiName,StringComparison.Ordinal);
        var xpDone=e.Contains(xpName,StringComparison.Ordinal);
        if(mainDone && uiDone && xpDone)
        {
            if(!s.Contains("SceneInteraction.GCET_IsUiActive()",StringComparison.Ordinal) ||
               !s.Contains("EarnXP.GCET_IsActive()",StringComparison.Ordinal) ||
               !ui.Contains("return shown or ui.hubShown or ui.refreshPending or ui.input",StringComparison.Ordinal) ||
               !e.Contains("return active == true",StringComparison.Ordinal))
                throw new InvalidOperationException("ELM idle stage partial source drift.");
            return SemanticInjectionResult.Skip("ELM source-owned idle stage already complete.");
        }
        if(!uiDone)
        {
            const string anchor="function SceneInteraction.Update()";
            if(ui.Split(anchor,StringSplitOptions.None).Length!=2 ||
               !ui.Contains("    ui.hideHub()",StringComparison.Ordinal) ||
               !ui.Contains("    shown = false",StringComparison.Ordinal))
                throw new InvalidOperationException("ELM Show/Hide ownership drifted.");
            ui=ui.Replace(anchor,
                "-- G-CET semantic:elm-immersiveway-idle-gc-v1\n"+
                "-- G-CET read-only authored native interaction visibility.\n"+
                uiName+"\n"+
                "  return shown or ui.hubShown or ui.refreshPending or ui.input\n"+
                "end\n\n"+anchor,StringComparison.Ordinal);
            context.Write(scene,ui);
        }
        else if(!ui.Contains("return shown or ui.hubShown or ui.refreshPending or ui.input",
                             StringComparison.Ordinal))
            throw new InvalidOperationException("ELM partial custom UI state.");

        if(!xpDone)
        {
            const string anchor="return EarnXP";
            if(e.Split(anchor,StringSplitOptions.None).Length!=2 ||
               !e.Contains("  active = true",StringComparison.Ordinal) ||
               !e.Contains("  active = false",StringComparison.Ordinal))
                throw new InvalidOperationException("ELM XP lifecycle drifted.");
            e=e.Replace(anchor,
                "-- G-CET semantic:elm-immersiveway-idle-gc-v1\n"+
                "-- G-CET read-only authored XP timer activity.\n"+
                xpName+"\n"+
                "  return active == true\n"+
                "end\n\n"+anchor,StringComparison.Ordinal);
            context.Write(xp,e);
        }
        else if(!e.Contains("return active == true",StringComparison.Ordinal))
            throw new InvalidOperationException("ELM partial XP activity state.");

        if(!mainDone)
        {
            // Original and generic/0-Engine staged callback registrars.
            var reg=new Regex(
                @"(?m)^[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*delta\s*\)[ \t]*$",
                RegexOptions.CultureInvariant);
            var matches=reg.Matches(s);
            if(matches.Count!=1 ||
               !s.Contains("  for i = #jobs, 1, -1 do",StringComparison.Ordinal) ||
               !s.Contains("    SceneInteraction.Update()",StringComparison.Ordinal) ||
               !s.Contains("safeCall(\"DarkFuture TickScene\"",StringComparison.Ordinal) ||
               !s.Contains("EarnMoney.Tick(delta)",StringComparison.Ordinal) ||
               !s.Contains("Jobs.Tick(delta)",StringComparison.Ordinal))
                throw new InvalidOperationException("ELM authored onUpdate services drifted.");
            var m=matches[0];
            const string guard=@" 
  -- G-CET semantic:elm-immersiveway-idle-gc-v1
  -- Original native/UI observers remain registered; this only guards
  -- empty maintenance, never active earning, scenes or delayed cleanup.
  if #jobs == 0
     and active.cloneID == nil and active.clone == nil
     and active.workspotID == nil and active.workspotSpawn == nil
     and active.workspot == nil and active.sceneLocation == nil
     and active.anim == nil and active.cameraInitRequest == nil
     and active.interactionID == nil and not active.freeflyActiveForScene
     and not flushingEtiquetteShowerActive
     and not active.darkFutureActive and active.dfInfoPanelSpawnRaw == nil
     and active.darkFutureRepeatTimer == nil
     and active.darkFutureFirstDelayTimer == nil
     and active.dfInfoPanelEntity == nil
     and not editorLiveMoveQueued
     and not SceneInteraction.GCET_IsUiActive()
     and not (EarnMoney and EarnMoney.IsActive and EarnMoney.IsActive())
     and not (EarnXP and EarnXP.GCET_IsActive and EarnXP.GCET_IsActive())
     and not (Jobs and Jobs.IsActive and Jobs.IsActive())
     and not (ToiletEffect and ToiletEffect.IsActive and ToiletEffect.IsActive())
  then return end";
            s=s[..(m.Index+m.Length)]+guard+s[(m.Index+m.Length)..];
            context.Write(init,s);
        }
        else if(!s.Contains("SceneInteraction.GCET_IsUiActive()",StringComparison.Ordinal) ||
                !s.Contains("EarnXP.GCET_IsActive()",StringComparison.Ordinal))
            throw new InvalidOperationException("ELM partial main idle guard.");

        return SemanticInjectionResult.Success(
            "ELM original interaction observers retained; idle onUpdate avoids "+
            "scene/UI/earnings/XP/jobs/toilet service work, with active timers "+
            "and original native interaction responsiveness unchanged.");
    }
}
