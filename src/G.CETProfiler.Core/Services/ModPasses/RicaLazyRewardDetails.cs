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
        // Match precisely the original getter group but tolerate the author's
        // indentation and source-format changes. Keep all statements in order.
        var pattern = new System.Text.RegularExpressions.Regex(
            @"(?m)^(?<indent>[ \t]*)pcall\(function\(\)\n" +
            @"(?<inner>[ \t]+)serial = system:GetPendingRewardSerial\(\)\n" +
            @"\k<inner>siteID = system:GetPendingRewardSiteID\(\)\n" +
            @"\k<inner>cycle = system:GetPendingRewardCycle\(\)\n" +
            @"\k<indent>end\)\n\k<indent>if serial <= 0 then break end",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var matches=pattern.Matches(source);
        if(matches.Count!=1)
            throw new InvalidOperationException("RICA pending reward getter grouping changed.");
        var patched=pattern.Replace(source,m=>{
            var indent=m.Groups["indent"].Value;
            var inner=m.Groups["inner"].Value;
            return indent+"-- G-CET semantic:rica-lazy-completion-details-v1\n"+
                indent+"pcall(function()\n"+
                inner+"serial = system:GetPendingRewardSerial()\n"+
                inner+"if serial > 0 then\n"+
                inner+"    siteID = system:GetPendingRewardSiteID()\n"+
                inner+"    cycle = system:GetPendingRewardCycle()\n"+
                inner+"end\n"+
                indent+"end)\n"+
                indent+"if serial <= 0 then break end";
        },1);
        context.Write(file,crlf?patched.Replace("\n","\r\n",StringComparison.Ordinal):patched);
        return SemanticInjectionResult.Success(
            "RICA skips two native reward-detail getters on empty polls; global replenishment and reward timing are untouched.");
    }
}
