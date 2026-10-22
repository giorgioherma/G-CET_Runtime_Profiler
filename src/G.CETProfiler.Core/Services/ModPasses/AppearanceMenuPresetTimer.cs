namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyAppearanceMenuPresetTimer(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "AMM.Props.presetLoadInProgress",
            "Util:FreezePlayer()",
            "Util:RemovePlayerEffects()");
        var text=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string marker="__gcetAmmPresetTimerV1";
        if(text.Contains(marker,StringComparison.Ordinal))
        {
            if(!text.Contains("AMM.__gcetPresetTimerTicket == ticket",StringComparison.Ordinal) ||
               !text.Contains("AMM.__gcetPresetTimerArmed = false",StringComparison.Ordinal))
                throw new InvalidOperationException("Partial AMM preset timer state.");
            return SemanticInjectionResult.Skip("AMM preset one-shot timer already installed.");
        }
        const string old=
            "\t\t\t\tif AMM.Props.presetLoadInProgress then\n" +
            "\t\t\t\t\tUtil:FreezePlayer()\n\n" +
            "\t\t\t\t\tCron.After(5, function()\n" +
            "\t\t\t\t\t\tAMM.Props.presetLoadInProgress = false\n" +
            "\t\t\t\t\t\tUtil:RemovePlayerEffects()\n" +
            "\t\t\t\t\tend)\n" +
            "\t\t\t\tend";
        const string updated=
            "\t\t\t\t-- "+marker+": one completion callback per preset-load cycle.\n" +
            "\t\t\t\tif AMM.Props.presetLoadInProgress then\n" +
            "\t\t\t\t\tUtil:FreezePlayer()\n" +
            "\t\t\t\t\tif not AMM.__gcetPresetTimerArmed then\n" +
            "\t\t\t\t\t\tlocal ticket = (AMM.__gcetPresetTimerTicket or 0) + 1\n" +
            "\t\t\t\t\t\tAMM.__gcetPresetTimerTicket = ticket\n" +
            "\t\t\t\t\t\tAMM.__gcetPresetTimerArmed = true\n" +
            "\t\t\t\t\t\tCron.After(5, function()\n" +
            "\t\t\t\t\t\t\tif AMM.__gcetPresetTimerArmed and AMM.__gcetPresetTimerTicket == ticket then\n" +
            "\t\t\t\t\t\t\t\tAMM.__gcetPresetTimerArmed = false\n" +
            "\t\t\t\t\t\t\t\tAMM.Props.presetLoadInProgress = false\n" +
            "\t\t\t\t\t\t\t\tUtil:RemovePlayerEffects()\n" +
            "\t\t\t\t\t\t\tend\n" +
            "\t\t\t\t\t\tend)\n" +
            "\t\t\t\t\tend\n" +
            "\t\t\t\telse\n" +
            "\t\t\t\t\tAMM.__gcetPresetTimerArmed = false\n" +
            "\t\t\t\tend";
        if(text.Split(old,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("AMM preset freeze/release source changed.");
        text=text.Replace(old,updated,StringComparison.Ordinal);
        if(file.Text.Contains("\r\n",StringComparison.Ordinal))
            text=text.Replace("\n","\r\n",StringComparison.Ordinal);
        context.Write(file,text);
        return SemanticInjectionResult.Success(
            "Original every-frame FreezePlayer maintained; completion timer " +
            "only once per preset-load cycle with generation-token guard against " +
            "stale callbacks after cancellation/restart.");
    }
}
