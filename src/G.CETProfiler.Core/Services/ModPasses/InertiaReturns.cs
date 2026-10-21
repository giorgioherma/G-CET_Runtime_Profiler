using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// The native locomotion observer is the required airborne wake: never pause it.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyInertiaGroundTransitionVelocity(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua","LocomotionEventsTransition",
            "AIR.last_ground_velocity","this.GetLinearVelocity(scriptInterface)",
            "AIR.was_on_ground");
        var source=file.Text;
        const string marker="G-CET semantic:inertia-ground-transition-velocity-v1";
        var completed=new Regex(
            @"(?m)^[ \t]*if\s+AIR\.was_on_ground\s+then\s*\r?\n"+
            @"[ \t]*--\s*"+Regex.Escape(marker)+@"\s*\r?\n"+
            @"[ \t]*local\s+locomotion_velocity\s*=\s*this\.GetLinearVelocity\(scriptInterface\)",
            RegexOptions.CultureInvariant);
        if(completed.Matches(source).Count==1)
            return SemanticInjectionResult.Skip("Inertia transition-only velocity getter already installed.");
        if(source.Contains(marker,StringComparison.Ordinal))
            throw new InvalidOperationException("Inertia partial/modified velocity stage preserved.");

        var declaration=new Regex(
            @"(?m)^[ \t]*local\s+locomotion_velocity\s*=\s*this\.GetLinearVelocity\(scriptInterface\)[ \t]*\r?\n",
            RegexOptions.CultureInvariant);
        var transition=new Regex(
            @"(?m)^(?<indent>[ \t]*)if\s+AIR\.was_on_ground\s+then[ \t]*\r?$",
            RegexOptions.CultureInvariant);
        var declarations=declaration.Matches(source);
        var transitions=transition.Matches(source);
        if(declarations.Count!=1 || transitions.Count!=1 ||
           declarations[0].Index>=transitions[0].Index ||
           Regex.Matches(source,@"\blocomotion_velocity\b").Count!=3 ||
           !source.Contains("if is_on_ground then",StringComparison.Ordinal) ||
           !source.Contains("if AIR.is_dodge or AIR.is_air_dodge then",StringComparison.Ordinal) ||
           !source.Contains("AIR.last_ground_velocity = player:GetVelocity()",StringComparison.Ordinal))
            throw new InvalidOperationException("Inertia ground-to-air ownership drift; refused.");

        var revised=source.Remove(declarations[0].Index,declarations[0].Length);
        var dest=transition.Match(revised);
        if(!dest.Success)throw new InvalidOperationException("Transition not found after removal.");
        var nl=source.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
        var indent=dest.Groups["indent"].Value;
        var child=indent+(indent.Contains('\t')?"\t":"    ");
        var addition=nl+child+"-- "+marker+nl+
            child+"local locomotion_velocity = this.GetLinearVelocity(scriptInterface)";
        revised=revised.Insert(dest.Index+dest.Length,addition);
        if(Regex.Matches(revised,@"\bGetLinearVelocity\s*\(").Count!=1 ||
           !completed.IsMatch(revised))
            throw new InvalidOperationException("Inertia native getter stage postcondition failed.");
        context.Write(file,revised);
        return SemanticInjectionResult.Success(
            "Elides native locomotion velocity reads on grounded/continuous-air/dodge ticks; "+
            "preserves original ground-to-air impulse, landing, wake and observer registration.");
    }
}
