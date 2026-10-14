using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyYaibaDistantBathtub(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "bathtub.lua", "function bathtub.update(player, pPos)",
            "ui.update()", "CONFIG.iconRange", "bathtub.hubVisible",
            "bathtub.pinID", "bathtub.waterApplied", "togglePin(near)",
            "buildHub()");
        var text = file.Text;
        const string marker = "G-CET semantic:yaiba-penthouse-distant-bathtub";
        var pattern = @"(?m)^(?<head>[ \t]*function[ \t]+bathtub[.]update[ \t]*\([ \t]*player[ \t]*,[ \t]*pPos[ \t]*\)[ \t]*\r?\n)(?<indent>[ \t]*)ui[.]update[ \t]*\([ \t]*\)[ \t]*";
        var regex = new Regex(pattern, RegexOptions.CultureInvariant);
        var matches = regex.Matches(text);
        if (matches.Count != 1)
            throw new InvalidOperationException(
                "Yaiba bathtub update shape drifted; refused native getter dormancy.");
        if (text.Contains(marker, StringComparison.Ordinal))
            throw new InvalidOperationException("Yaiba distant-bathtub pass already present.");

        // No code is moved/deleted below the early-out. Unchanged author
        // handling resumes at ui.update for all nearby/active states.
        var m = matches[0];
        var indent = m.Groups["indent"].Value;
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var gate = string.Join(newline, new [] {
            indent + "-- " + marker,
            indent + "-- Only skip when no bath UI, mappin or water reapply is pending.",
            indent + "if player and pPos and not bathtub.hubVisible and not ui.hubShown and not bathtub.pinID and not bathtub.waterApplied then",
            indent + "    local __gcetBathPos = CONFIG.position",
            indent + "    local __gcetDx = pPos.x - __gcetBathPos.x",
            indent + "    local __gcetDy = pPos.y - __gcetBathPos.y",
            indent + "    local __gcetDz = pPos.z + 1.0 - __gcetBathPos.z",
            indent + "    if __gcetDx * __gcetDx + __gcetDy * __gcetDy + __gcetDz * __gcetDz > (CONFIG.iconRange + 1.0)^2 then return end",
            indent + "end",
            indent + "ui.update()"
        });
        text = text[..m.Index] + m.Groups["head"].Value + gate +
               text[(m.Index+m.Length)..];
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Removed per-frame native UI/hub polling while distant from Yaiba bathtub " +
            "and without active hub, icon or water state; all active interactions unchanged.");
    }

}
