namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyTunnelMissingHooksDiagnostics(
        SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local function status()",
            "local function update(dt)",
            "Native TRQHooks unavailable. Native scripts need to reload.",
            "H.diag=H.diag+dt");
        const string marker="__gcetTunnelMissingHooksDiagV1";
        var text=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        if(text.Contains(marker,StringComparison.Ordinal))
        {
            if(!text.Contains("__gcetTunnelMissingHooksReported and 15 or 3",StringComparison.Ordinal) ||
               !text.Contains("__gcetTunnelMissingHooksReported=false",StringComparison.Ordinal))
                throw new InvalidOperationException("Partial Tunnel missing-hooks diagnostic stage.");
            return SemanticInjectionResult.Skip("Tunnel missing-hooks diagnostic interval already present.");
        }
        const string missing =
            "if not valid(H.hooks)then H.error='Native TRQHooks unavailable. Native scripts need to reload.';if H.diag>3 then H.diag=0;status()end;return end";
        const string update =
            "local function update(dt)\n";
        if(text.Split(missing,StringSplitOptions.None).Length!=2 ||
           text.Split(update,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Tunnel error status and update source changed.");
        const string replacement =
            "if not valid(H.hooks)then\n" +
            "  H.error='Native TRQHooks unavailable. Native scripts need to reload.'\n" +
            "  -- "+marker+": report once after 3s, then at most every 15s.\n" +
            "  -- Always check native readiness at the original author cadence.\n" +
            "  if H.diag>(__gcetTunnelMissingHooksReported and 15 or 3)then\n" +
            "   H.diag=0;status();__gcetTunnelMissingHooksReported=true\n" +
            "  end\n" +
            "  return\n" +
            " end\n" +
            " __gcetTunnelMissingHooksReported=false";
        text=text.Replace(update,
            "local __gcetTunnelMissingHooksReported=false\n"+update,
            StringComparison.Ordinal);
        text=text.Replace(missing,replacement,StringComparison.Ordinal);
        if(file.Text.Contains("\r\n",StringComparison.Ordinal))
            text=text.Replace("\n","\r\n",StringComparison.Ordinal);
        context.Write(file,text);
        return SemanticInjectionResult.Success(
            "Preserved every author native-hook readiness probe, first 3-second " +
            "missing-hook report and normal quest cadence. After a persistent " +
            "missing TRQHooks state, expensive full JSON/navigation diagnostic " +
            "serialization and disk writes run at most every 15 seconds; " +
            "recovery resets the reporter. Does not fix TRQHooks availability.");
    }
}
