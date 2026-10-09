using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal sealed record InteractionUiIdleGuardProof(
    string FunctionName,
    string GateExpression,
    string IdleResetStatement,
    string HubVariable);

internal sealed record CrossFileInteractionUiCallProof(
    string FunctionName,
    string Receiver,
    string GateExpression,
    string PendingExpression,
    string IdleResetStatement,
    string ModuleRelativeFile,
    string ModuleSha256);

internal static class GenericInteractionUiTransform
{
    private static readonly Regex CallbackCall = new(
        @"\b(?<function>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+)\s*\(\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HubRead = new(
        @"^local\s+(?<variable>[A-Za-z_]\w*)\s*=\s*getDialogChoiceHubs\s*\(\s*\)\s*;?\s*(?:--.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FalseReset = new(
        @"^(?<lhs>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+)\s*=\s*false\s*;?\s*(?:--.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CallOnlyOnDraw = new(
        @"(?ms)^(?<opening>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onDraw[""']\s*,\s*function\s*\(\s*\)\s*\r?\n)" +
        @"(?<indent>[ \t]*)if\s+(?<receiver>[A-Za-z_]\w*)\s+and\s+\k<receiver>\.(?<method>[A-Za-z_]\w*)\s+then\s+\k<receiver>\.\k<method>\s*\(\s*\)\s+end\s*\r?\n" +
        @"(?<close>[ \t]*end\s*\)\s*;?\s*(?:--[^\r\n]*)?$)",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline |
        RegexOptions.Singleline);

    internal static bool TryProveForCallback(
        string fullText,
        string callbackText,
        out InteractionUiIdleGuardProof proof)
    {
        proof = new InteractionUiIdleGuardProof("", "", "", "");
        if (string.IsNullOrWhiteSpace(fullText) ||
            string.IsNullOrWhiteSpace(callbackText))
            return false;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match call in CallbackCall.Matches(callbackText))
        {
            var functionName = call.Groups["function"].Value;
            if (!seen.Add(functionName))
                continue;

            if (TryProveFunction(fullText, functionName, out var shape))
            {
                proof = shape.Proof;
                return true;
            }
        }

        return false;
    }

    internal static bool TryProveNamedFunction(
        string fullText,
        string functionName,
        out InteractionUiIdleGuardProof proof)
    {
        proof = new InteractionUiIdleGuardProof("", "", "", "");
        if (!TryProveFunction(fullText, functionName, out var shape))
            return false;

        proof = shape.Proof;
        return true;
    }

    internal static bool TryProveCrossFileCall(
        string callbackText,
        string moduleText,
        string moduleRelativeFile,
        string moduleSha256,
        out CrossFileInteractionUiCallProof proof)
    {
        proof = new CrossFileInteractionUiCallProof(
            "", "", "", "", "", "", "");

        var normalizedCallback = Normalize(callbackText);
        var match = CallOnlyOnDraw.Match(normalizedCallback);
        if (!match.Success ||
            match.Index != 0 ||
            match.Length != normalizedCallback.Length)
            return false;

        var receiver = match.Groups["receiver"].Value;
        var method = match.Groups["method"].Value;
        var functionName = receiver + "." + method;

        if (!TryProveNamedFunction(moduleText, functionName, out var functionProof))
            return false;

        if (!functionProof.GateExpression.StartsWith(
                receiver + ".",
                StringComparison.Ordinal) ||
            !functionProof.IdleResetStatement.StartsWith(
                receiver + ".",
                StringComparison.Ordinal))
            return false;

        var equals = functionProof.IdleResetStatement.IndexOf(
            '=',
            StringComparison.Ordinal);
        if (equals <= 0)
            return false;
        var pendingExpression =
            functionProof.IdleResetStatement[..equals].Trim();

        var normalizedModule = Normalize(moduleText);
        if (!Regex.IsMatch(
                normalizedModule,
                @"(?m)^[ \t]*return\s+" + Regex.Escape(receiver) + @"\s*(?:--.*)?$",
                RegexOptions.CultureInvariant))
            return false;

        // A hidden callback can only be skipped if the visible state can be
        // changed independently of that callback. The proved update body never
        // writes the gate true, so this writer necessarily lives elsewhere in
        // the module.
        if (!Regex.IsMatch(
                normalizedModule,
                @"\b" + Regex.Escape(functionProof.GateExpression) +
                @"\s*=\s*true\b",
                RegexOptions.CultureInvariant))
            return false;

        proof = new CrossFileInteractionUiCallProof(
            functionName,
            receiver,
            functionProof.GateExpression,
            pendingExpression,
            functionProof.IdleResetStatement,
            moduleRelativeFile,
            moduleSha256);
        return true;
    }

    internal static bool TryApplyCrossFileCallGuard(
        string fullText,
        CrossFileInteractionUiCallProof expected,
        out string transformed)
    {
        transformed = fullText;
        var normalized = Normalize(fullText);
        var method = expected.FunctionName[
            (expected.FunctionName.LastIndexOf('.') + 1)..];

        var matches = CallOnlyOnDraw.Matches(normalized)
            .Cast<Match>()
            .Where(m =>
                m.Groups["receiver"].Value.Equals(
                    expected.Receiver,
                    StringComparison.Ordinal) &&
                m.Groups["method"].Value.Equals(
                    method,
                    StringComparison.Ordinal))
            .ToList();

        if (matches.Count != 1)
            return false;

        var match = matches[0];
        var guarded =
            match.Groups["opening"].Value +
            match.Groups["indent"].Value +
            "if " + expected.Receiver +
            " and " + expected.FunctionName +
            " and (" + expected.GateExpression +
            " or " + expected.PendingExpression +
            ") then " + expected.FunctionName + "() end\n" +
            match.Groups["close"].Value;

        transformed =
            normalized[..match.Index] +
            guarded +
            normalized[(match.Index + match.Length)..];
        return true;
    }

    internal static bool TryApply(
        string fullText,
        InteractionUiIdleGuardProof expected,
        out string transformed)
    {
        transformed = fullText;
        if (string.IsNullOrWhiteSpace(expected.FunctionName) ||
            !TryProveFunction(fullText, expected.FunctionName, out var shape))
            return false;

        var actual = shape.Proof;
        if (!actual.FunctionName.Equals(expected.FunctionName, StringComparison.Ordinal) ||
            !actual.GateExpression.Equals(expected.GateExpression, StringComparison.Ordinal) ||
            !actual.IdleResetStatement.Equals(expected.IdleResetStatement, StringComparison.Ordinal) ||
            !actual.HubVariable.Equals(expected.HubVariable, StringComparison.Ordinal))
            return false;

        var lines = Normalize(fullText).Split('\n').ToList();
        if (shape.GetterLine < 0 || shape.GetterLine >= lines.Count)
            return false;

        var indent = LeadingWhitespace(lines[shape.GetterLine]);
        lines.InsertRange(
            shape.GetterLine,
            new[]
            {
                indent + $"if not {actual.GateExpression} then",
                indent + "    " + actual.IdleResetStatement,
                indent + "    return",
                indent + "end -- G-CET interaction UI idle guard"
            });

        transformed = string.Join("\n", lines);
        return true;
    }

    private static bool TryProveFunction(
        string fullText,
        string functionName,
        out InteractionUiFunctionShape shape)
    {
        shape = new InteractionUiFunctionShape(
            new InteractionUiIdleGuardProof("", "", "", ""),
            -1);

        var normalized = Normalize(fullText);
        var lines = normalized.Split('\n');
        var escapedFunction = Regex.Escape(functionName);
        var definitions = new List<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(
                    lines[i],
                    @"^[ \t]*function\s+" + escapedFunction + @"\s*\(\s*\)\s*(?:--.*)?$",
                    RegexOptions.CultureInvariant))
                definitions.Add(i);
        }

        if (definitions.Count != 1)
            return false;

        var functionStart = definitions[0];
        var functionIndent = LeadingWhitespace(lines[functionStart]);
        var functionEnd = -1;
        for (var i = functionStart + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (!Regex.IsMatch(
                    trimmed,
                    @"^end\s*(?:--.*)?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            if (LeadingWhitespace(lines[i]).Equals(functionIndent, StringComparison.Ordinal))
            {
                functionEnd = i;
                break;
            }
        }

        if (functionEnd <= functionStart + 2)
            return false;

        var meaningful = Enumerable.Range(
                functionStart + 1,
                functionEnd - functionStart - 1)
            .Where(i =>
            {
                var trimmed = lines[i].Trim();
                return trimmed.Length > 0 &&
                       !trimmed.StartsWith("--", StringComparison.Ordinal);
            })
            .ToArray();

        if (meaningful.Length < 3)
            return false;

        var getterLine = meaningful[0];
        var getterIndent = LeadingWhitespace(lines[getterLine]);
        if (getterIndent.Length <= functionIndent.Length)
            return false;

        var getter = HubRead.Match(lines[getterLine].Trim());
        if (!getter.Success)
            return false;

        var hubVariable = getter.Groups["variable"].Value;
        var branchLine = meaningful[1];
        if (!LeadingWhitespace(lines[branchLine]).Equals(getterIndent, StringComparison.Ordinal) ||
            !TryReadGate(lines[branchLine].Trim(), "if", null, out var gate))
            return false;

        if (!gate.EndsWith(".hubShown", StringComparison.Ordinal))
            return false;

        var receiver = functionName[..functionName.LastIndexOf('.')];
        if (!gate.StartsWith(receiver + ".", StringComparison.Ordinal))
            return false;

        var branchEnd = -1;
        for (var i = branchLine + 1; i < functionEnd; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (!LeadingWhitespace(lines[i]).Equals(getterIndent, StringComparison.Ordinal))
                continue;

            if (trimmed.StartsWith("elseif ", StringComparison.Ordinal))
            {
                if (!TryReadGate(trimmed, "elseif", gate, out _))
                    return false;
                continue;
            }

            if (Regex.IsMatch(
                    trimmed,
                    @"^else\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return false;

            if (Regex.IsMatch(
                    trimmed,
                    @"^end\s*(?:--.*)?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                branchEnd = i;
                break;
            }

            return false;
        }

        if (branchEnd < 0)
            return false;

        var hubUse = new Regex(
            @"\b" + Regex.Escape(hubVariable) + @"\b",
            RegexOptions.CultureInvariant);
        var guardedHubUses = 0;
        for (var i = getterLine + 1; i < functionEnd; i++)
        {
            if (!hubUse.IsMatch(lines[i]))
                continue;

            if (i < branchLine || i > branchEnd)
                return false;

            guardedHubUses++;
        }

        if (guardedHubUses == 0)
            return false;

        var tail = Enumerable.Range(
                branchEnd + 1,
                functionEnd - branchEnd - 1)
            .Where(i =>
            {
                var trimmed = lines[i].Trim();
                return trimmed.Length > 0 &&
                       !trimmed.StartsWith("--", StringComparison.Ordinal);
            })
            .ToArray();

        if (tail.Length != 1 ||
            !LeadingWhitespace(lines[tail[0]]).Equals(getterIndent, StringComparison.Ordinal))
            return false;

        var reset = FalseReset.Match(lines[tail[0]].Trim());
        if (!reset.Success)
            return false;

        var resetLhs = reset.Groups["lhs"].Value;
        if (!resetLhs.StartsWith(receiver + ".", StringComparison.Ordinal))
            return false;

        shape = new InteractionUiFunctionShape(
            new InteractionUiIdleGuardProof(
                functionName,
                gate,
                resetLhs + " = false",
                hubVariable),
            getterLine);
        return true;
    }

    private static bool TryReadGate(
        string line,
        string keyword,
        string? expectedGate,
        out string gate)
    {
        gate = "";
        var match = Regex.Match(
            line,
            "^" + Regex.Escape(keyword) + @"\s+(?<condition>.+)\s+then\s*(?:--.*)?$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        var condition = match.Groups["condition"].Value.Trim();
        var first = Regex.Match(
            condition,
            @"^(?<gate>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+)(?:\s+and\b.*)?$",
            RegexOptions.CultureInvariant);
        if (!first.Success)
            return false;

        gate = first.Groups["gate"].Value;
        return expectedGate is null ||
               gate.Equals(expectedGate, StringComparison.Ordinal);
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string LeadingWhitespace(string line) =>
        Regex.Match(line, @"^[ \t]*", RegexOptions.CultureInvariant).Value;

    private sealed record InteractionUiFunctionShape(
        InteractionUiIdleGuardProof Proof,
        int GetterLine);
}
