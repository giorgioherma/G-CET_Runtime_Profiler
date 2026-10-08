using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal sealed record UiVisibilityDormancyProof(
    string GateExpression,
    string WakeKind);

internal static class GenericUiVisibilityTransform
{
    private static readonly Regex Wrapper = new(
        @"(?ms)^(?<opening>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onDraw[""']\s*,\s*function\s*\(\s*\)\s*)\r?\n" +
        @"(?<prefix>(?:[ \t]*(?:--[^\r\n]*)?\r?\n)*)" +
        @"(?<indent>[ \t]*)if\s+(?<gate>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s+then\s*\r?\n" +
        @"(?<body>.*)" +
        @"^\k<indent>end\s*\r?\n" +
        @"(?<suffix>(?:[ \t]*(?:--[^\r\n]*)?\r?\n)*)" +
        @"(?<close>[ \t]*end\s*\)\s*;?\s*(?:--[^\r\n]*)?$)",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline |
        RegexOptions.Singleline);

    internal static bool TryProveForCallback(
        string fullText,
        string callbackText,
        out UiVisibilityDormancyProof proof)
    {
        proof = new UiVisibilityDormancyProof("", "");
        var normalized = Normalize(callbackText);
        var match = Wrapper.Match(normalized);
        if (!match.Success || match.Index != 0 || match.Length != normalized.Length)
            return false;

        var gate = match.Groups["gate"].Value;
        if (string.IsNullOrWhiteSpace(gate))
            return false;

        // The visible branch must contain actual UI work; do not classify a
        // random state gate as UI merely because it is in onDraw.
        var body = match.Groups["body"].Value;
        if (!Regex.IsMatch(
                body,
                @"\b(?:ImGui\.|Draw\w*\s*\(|Render\w*\s*\(|\.Draw\s*\(|\.Render\s*\()",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        // Wake must be independently writable outside the draw callback.
        var outside = Normalize(fullText).Replace(normalized, "", StringComparison.Ordinal);
        var escapedGate = Regex.Escape(gate);
        var writer = new Regex(
            @"(?ms)(?<wake>registerInput|registerHotkey|onOverlayOpen|onOverlayClose).{0,800}?\b" +
            escapedGate +
            @"\s*=\s*(?:true|not\s+" +
            escapedGate +
            @")\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var wake = writer.Match(outside);
        if (!wake.Success)
            return false;

        proof = new UiVisibilityDormancyProof(
            gate,
            wake.Groups["wake"].Value);
        return true;
    }

    internal static bool TryApply(
        string fullText,
        UiVisibilityDormancyProof expected,
        out string rewritten)
    {
        rewritten = fullText;
        var normalized = Normalize(fullText);
        var matches = Wrapper.Matches(normalized)
            .Cast<Match>()
            .Where(m =>
                m.Groups["gate"].Value.Equals(
                    expected.GateExpression,
                    StringComparison.Ordinal))
            .ToList();

        if (matches.Count != 1)
            return false;

        var m = matches[0];
        var replacement =
            m.Groups["opening"].Value + "\n" +
            m.Groups["prefix"].Value +
            m.Groups["indent"].Value +
            "if not " + expected.GateExpression + " then return end\n" +
            m.Groups["body"].Value +
            m.Groups["suffix"].Value +
            m.Groups["close"].Value;

        rewritten =
            normalized[..m.Index] +
            replacement +
            normalized[(m.Index + m.Length)..];
        return true;
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
