namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyRicaLazyRewardDetails(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local function processCompletionRewards(",
            "GetPendingRewardSerial()",
            "GetPendingRewardSiteID()",
            "GetPendingRewardCycle()");
        const string marker="-- G-CET semantic:rica-lazy-completion-details-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("RICA lazy completion getters already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var source=crlf?file.Text.Replace("\r\n","\n",StringComparison.Ordinal):file.Text;
        const string before="""
        pcall(function()
            serial = system:GetPendingRewardSerial()
            siteID = system:GetPendingRewardSiteID()
            cycle = system:GetPendingRewardCycle()
        end)
        if serial <= 0 then break end
""";
        const string after="""
        -- G-CET semantic:rica-lazy-completion-details-v1
        -- Do not decode completion metadata for an empty queue.
        -- The original polling cadence and all success/error paths are retained.
        pcall(function()
            serial = system:GetPendingRewardSerial()
            if serial > 0 then
                siteID = system:GetPendingRewardSiteID()
                cycle = system:GetPendingRewardCycle()
            end
        end)
        if serial <= 0 then break end
""";
        if(source.Split(before,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("RICA pending reward getter grouping changed.");
        var patched=source.Replace(before,after,StringComparison.Ordinal);
        context.Write(file,crlf?patched.Replace("\n","\r\n",StringComparison.Ordinal):patched);
        return SemanticInjectionResult.Success(
            "RICA skips two native reward-detail getters on empty polls; global replenishment and reward timing are untouched.");
    }
}
