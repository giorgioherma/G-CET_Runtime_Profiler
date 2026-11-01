namespace GCETRuntimeProfiler.Core.Services;

// Dedra JNC Joytoy author-owned UI: the CET draw registration stays live.
// The hidden custom hub cannot consume or adjust dialog-choice blackboards,
// but the author's per-frame input latch still must be cleared.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyDedraJncJoytoyInactiveUiDraw(
        SemanticPatchContext context)
    {
        var init = context.FindFile("init.lua",
            "ui.update()", "if not sessionActive then return end",
            "registerForEvent(\"onShutdown\"");
        var ui = context.FindFile("modules/interactionUI.lua",
            "function ui.update()", "local hubs = getDialogChoiceHubs()",
            "ui.hubShown", "ui.input = false");

        // Generic frame registration may already have rewritten
        // registerForEvent("onDraw", ...) on the current composed source.
        // Prove the actual author callback body rather than its registrar.
        const string drawBody =
            "  if shuttingDown then return end\n" +
            "  if not sessionActive then return end\n\n" +
            "  ui.update()\nend)";
        var normalizedInit = init.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalizedInit.Split(drawBody, StringSplitOptions.None).Length != 2 ||
            normalizedInit.Split("ui.update()", StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException(
                "Dedra JNC Joytoy draw UI callback ownership drifted.");

        var crlf = ui.Text.Contains("\r\n", StringComparison.Ordinal);
        var current = ui.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string pristine =
            "function ui.update() -- Run ever frame to avoid unwanted changes\n" +
            "    local hubs = getDialogChoiceHubs()\n";
        const string completed =
            "function ui.update() -- Run ever frame to avoid unwanted changes\n" +
            "    -- __gcetDedraJncInactiveDrawV1: no hidden-hub blackboard snapshot.\n" +
            "    if not ui.hubShown then\n" +
            "        ui.input = false -- preserve per-frame double-input latch reset\n" +
            "        return\n" +
            "    end\n" +
            "    local hubs = getDialogChoiceHubs()\n";
        const string tag = "__gcetDedraJncInactiveDrawV1";
        const string tail = "    ui.input = false -- Avoid double input\nend\n\nreturn ui";

        // Do not suppress the author's update/hide pathways or fail open
        // on a half-generated source. No extra CET callbacks or workers.
        if (current.Split("function ui.update()", StringSplitOptions.None).Length != 2 ||
            !current.Contains(tail, StringComparison.Ordinal) ||
            !current.Contains("function ui.showHub()", StringComparison.Ordinal) ||
            !current.Contains("function ui.hideHub()", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Dedra JNC Joytoy UI hub/input lifecycle no longer source-proven.");
        if (current.Contains(tag, StringComparison.Ordinal))
        {
            if (current.Split(tag, StringSplitOptions.None).Length != 2 ||
                !current.Contains(completed, StringComparison.Ordinal) ||
                current.Contains(pristine, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Dedra JNC Joytoy hidden-hub optimization incomplete.");
            return SemanticInjectionResult.Skip(
                "Dedra JNC Joytoy UI hidden-hub draw fast path already complete.");
        }
        if (current.Split(pristine, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException(
                "Dedra JNC Joytoy UI author draw body changed; refuse source drift.");

        var changed = current.Replace(pristine, completed, StringComparison.Ordinal);
        context.Write(ui, crlf
            ? changed.Replace("\n", "\r\n", StringComparison.Ordinal)
            : changed);
        return SemanticInjectionResult.Success(
            "Source-proven hidden JNC Joytoy UI avoids per-draw dialog blackboard " +
            "materialization; visible hub logic and input latch are retained.");
    }
}
