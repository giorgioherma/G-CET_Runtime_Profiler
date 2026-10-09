using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal sealed record HardDormantGuardProof(
    string GateExpression,
    int PreGuardReadCount);

internal static class GenericHardDormantGuardTransform
{
    internal static bool TryApply(
        string callbackText,
        HardDormantGuardProof expected,
        out string rewritten)
    {
        rewritten = callbackText;
        if (string.IsNullOrWhiteSpace(callbackText) ||
            string.IsNullOrWhiteSpace(expected.GateExpression) ||
            expected.PreGuardReadCount <= 0)
            return false;

        var normalized = callbackText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();

        var openingIndex = -1;
        var openingRegex = new Regex(
            @"(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        for (var i = 0; i < lines.Count; i++)
        {
            if (openingRegex.IsMatch(lines[i]))
            {
                if (openingIndex >= 0)
                    return false;
                openingIndex = i;
            }
        }
        if (openingIndex < 0)
            return false;

        var escapedGate = Regex.Escape(expected.GateExpression);
        var gateRegex = new Regex(
            @"^\s*if\s+not\s+" + escapedGate + @"\s+then\s+return\s+end\s*;?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var gateIndex = -1;
        for (var i = openingIndex + 1; i < lines.Count; i++)
        {
            if (!gateRegex.IsMatch(lines[i]))
                continue;
            if (gateIndex >= 0)
                return false;
            gateIndex = i;
        }
        if (gateIndex <= openingIndex + 1)
            return false;

        var gateRoot = expected.GateExpression.Split('.')[0];
        var readCount = 0;
        var meaningfulCount = 0;

        for (var i = openingIndex + 1; i < gateIndex; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal))
                continue;

            meaningfulCount++;

            if (Regex.IsMatch(
                    line,
                    @"^local\s+" + Regex.Escape(gateRoot) + @"\b",
                    RegexOptions.CultureInvariant))
                return false;

            if (!Regex.IsMatch(
                    line,
                    @"^local\s+[A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*\s*=\s*.+$",
                    RegexOptions.CultureInvariant))
                return false;

            var callCount = Regex.Matches(
                    line,
                    @"[A-Za-z_][\w.:]*\s*\(",
                    RegexOptions.CultureInvariant)
                .Count;
            if (callCount == 0)
            {
                if (!Regex.IsMatch(
                        line,
                        @"^local\s+[A-Za-z_]\w*\s*=\s*(?:nil|true|false|[-+]?\d+(?:\.\d+)?|['""][^'""]*['""]|[A-Za-z_]\w*)\s*;?$",
                        RegexOptions.CultureInvariant))
                    return false;
                continue;
            }

            if (callCount != 1 ||
                !Regex.IsMatch(
                    line,
                    @"(?:\bGame\.Get[A-Za-z_]\w*\s*\(|\b__gcetGet[A-Za-z_]\w*\s*\(|\bGetSingleton\s*\()",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return false;

            readCount++;
        }

        if (meaningfulCount == 0 || readCount != expected.PreGuardReadCount)
            return false;

        var gateLine = lines[gateIndex];
        lines.RemoveAt(gateIndex);
        lines.Insert(openingIndex + 1, gateLine);

        rewritten = string.Join("\n", lines);
        return true;
    }
}
