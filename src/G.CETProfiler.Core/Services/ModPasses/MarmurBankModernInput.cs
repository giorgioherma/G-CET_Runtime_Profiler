using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Modern MarmurBank source families (21b / author 22.5 sample).
// Do not remove the world-suppression override: it is needed when
// the bank hub owns the vanilla interaction UI.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyMarmurBankModernInput(SemanticPatchContext context)
    {
        var file=context.FindFile("external/interactionUI.lua",
            "WORLD_INTERACTION_ACTIONS","shouldBlockWorldAction",
            "worldActionOverrideOk","ui.hubShown");
        var source=file.Text;
        const string marker="-- G-CET semantic:marmurbank-modern-input-v1";
        if(source.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Modern MarmurBank selective action stage already installed.");
        var overrideRx=new Regex(
            @"(?m)^(?<indent>[ \t]*)if action then[ \t]*\r?\n(?<child>[ \t]*)local actionName, actionType = getActionDetails\(action\)",
            RegexOptions.CultureInvariant);
        var observerRx=new Regex(
            @"Observe\s*\(\s*['""]PlayerPuppet['""]\s*,\s*['""]OnAction['""]\s*,\s*function\(_, action\)",
            RegexOptions.CultureInvariant);
        var closingRx=new Regex(
            @"(?m)^(?<indent>[ \t]*)end\)\s*\r?\n(?<space>[ \t]*\r?\n)(?<next>[ \t]*Override\([""']InteractionUIBase[""']\s*,\s*[""']OnDialogsSelectIndex[""'])",
            RegexOptions.CultureInvariant);
        if(overrideRx.Matches(source).Count!=1 ||
           observerRx.Matches(source).Count!=1 ||
           closingRx.Matches(source).Count!=1 ||
           !source.Contains("if shouldBlockWorldAction(getActionDetails(action)) then",StringComparison.Ordinal) ||
           !source.Contains("if ui.input or not ui.hubShown then",StringComparison.Ordinal) ||
           !source.Contains("local worldActionOverrideOk = pcall(function()",StringComparison.Ordinal))
            throw new InvalidOperationException("Modern MarmurBank action/suppression source shape changed; refused.");
        var nl=source.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
        var output=overrideRx.Replace(source,m=>
            m.Groups["indent"].Value+"if action and shouldSuppressVanillaNow() then"+nl+
            m.Groups["child"].Value+"local actionName, actionType = getActionDetails(action)",1);
        output=output.Replace(
            "if shouldBlockWorldAction(getActionDetails(action)) then",
            "if not worldActionOverrideOk and shouldSuppressVanillaNow() and shouldBlockWorldAction(getActionDetails(action)) then",
            StringComparison.Ordinal);
        output=observerRx.Replace(output,"local function __gcetMarmurBankOnAction(_, action)",1);
        var router=String.Join(nl,new[]{
            marker,
            "\tlocal __gcetMarmurBankRouted = false",
            "\tlocal __gcetMarmurBankEngineOk, __gcetMarmurBankEngine = pcall(GetMod, \"0-Engine\")",
            "\tif __gcetMarmurBankEngineOk and type(__gcetMarmurBankEngine) == \"table\" then",
            "\t\tlocal api = __gcetMarmurBankEngine",
            "\t\tif type(api.GCET) == \"table\" and type(api.GCET.SubscribeAction) == \"function\" then api = api.GCET end",
            "\t\tif type(api.SubscribeAction) == \"function\" then",
            "\t\t\tlocal actions = { \"ChoiceScrollUp\", \"ChoiceScrollDown\", \"ChoiceApply\" }",
            "\t\t\tfor name in pairs(WORLD_INTERACTION_ACTIONS) do",
            "\t\t\t\tif name ~= \"ChoiceScrollUp\" and name ~= \"ChoiceScrollDown\" and name ~= \"ChoiceApply\" then",
            "\t\t\t\t\tactions[#actions + 1] = name",
            "\t\t\t\tend",
            "\t\t\tend",
            "\t\t\tlocal ok, handle = pcall(api.SubscribeAction, {",
            "\t\t\t\tid = \"MarmurBank.InteractionUI.OnAction\", actions = actions",
            "\t\t\t}, __gcetMarmurBankOnAction)",
            "\t\t\t__gcetMarmurBankRouted = ok and handle ~= nil and handle ~= false",
            "\t\tend",
            "\tend",
            "\tif not __gcetMarmurBankRouted then",
            "\t\tObserve('PlayerPuppet', 'OnAction', __gcetMarmurBankOnAction)",
            "\tend",
            ""
        });
        var closer=closingRx.Match(output);
        if(!closer.Success)throw new InvalidOperationException("Modern MarmurBank observer closing disappeared.");
        output=closingRx.Replace(output,m=>
            m.Groups["indent"].Value+"end"+nl+nl+router+
            m.Groups["next"].Value,1);
        if(!output.Contains(marker,StringComparison.Ordinal) ||
           !output.Contains("if action and shouldSuppressVanillaNow() then",StringComparison.Ordinal) ||
           Regex.Matches(output,@"Observe\s*\(\s*['""]PlayerPuppet['""]\s*,\s*['""]OnAction['""]\s*,\s*function").Count!=0 ||
           !output.Contains("if not __gcetMarmurBankRouted then",StringComparison.Ordinal))
            throw new InvalidOperationException("Modern MarmurBank selective routing postcondition failed.");
        context.Write(file,output);
        return SemanticInjectionResult.Success(
            "The world-interaction override remains available for active suppression, but "+
            "no longer decodes idle actions. Only banker/finite world actions are routed "+
            "via the method-resolved 0-Engine subscriber with the original CET fallback.");
    }
}
