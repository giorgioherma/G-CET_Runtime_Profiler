using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal sealed record SequencePollingProof(
    string SystemName,
    string BridgeVariable,
    string SequenceVariable,
    string SequenceMember,
    string LastVariable,
    bool WrappedPcall);

internal static class GenericSequencePollingTransform
{
    private static readonly Regex PcallShape = new(
        @"(?ms)(?<indent>^[ \t]*)local\s+(?<ok>[A-Za-z_]\w*)\s*,\s*(?<bridge>[A-Za-z_]\w*)\s*=\s*pcall\s*\(\s*function\s*\(\s*\)\s*\r?\n" +
        @"[ \t]*local\s+(?<container>[A-Za-z_]\w*)\s*=\s*Game\.GetScriptableSystemsContainer\s*\(\s*\)\s*\r?\n" +
        @"[ \t]*if\s+not\s+\k<container>\s+then\s+return\s+nil\s+end\s*\r?\n" +
        @"[ \t]*return\s+\k<container>:Get\s*\(\s*CName\.new\s*\(\s*[""'](?<system>[^""']+)[""']\s*\)\s*\)\s*\r?\n" +
        @"[ \t]*end\s*\)\s*\r?\n" +
        @"[ \t]*if\s+not\s+\k<ok>\s+or\s+not\s+\k<bridge>\s+then\s+return\s+end\s*\r?\n" +
        @"[ \t]*local\s+(?<seq>[A-Za-z_]\w*)\s*=\s*\k<bridge>\.(?<member>[A-Za-z_]\w*)\s*\r?\n" +
        @"[ \t]*if\s+\k<seq>\s*==\s*(?<last>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s+then\s+return\s+end\s*\r?\n" +
        @"[ \t]*\k<last>\s*=\s*\k<seq>\s*$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline |
        RegexOptions.Singleline);

    private static readonly Regex DirectShape = new(
        @"(?ms)(?<indent>^[ \t]*)local\s+(?<bridge>[A-Za-z_]\w*)\s*=\s*Game\.GetScriptableSystemsContainer\s*\(\s*\):Get\s*\(\s*CName\.new\s*\(\s*[""'](?<system>[^""']+)[""']\s*\)\s*\)\s*\r?\n" +
        @"[ \t]*if\s+not\s+\k<bridge>\s+then\s+return\s+end\s*\r?\n" +
        @"[ \t]*local\s+(?<seq>[A-Za-z_]\w*)\s*=\s*\k<bridge>\.(?<member>[A-Za-z_]\w*)\s*\r?\n" +
        @"[ \t]*if\s+\k<seq>\s*==\s*(?<last>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s+then\s+return\s+end\s*\r?\n" +
        @"[ \t]*\k<last>\s*=\s*\k<seq>\s*$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline |
        RegexOptions.Singleline);

    internal static bool TryProveForCallback(
        string callbackText,
        out SequencePollingProof proof)
    {
        proof = new SequencePollingProof("", "", "", "", "", false);
        var text = Normalize(callbackText);

        var matches = PcallShape.Matches(text).Cast<Match>().ToList();
        if (matches.Count == 1)
        {
            proof = From(matches[0], true);
            return true;
        }
        if (matches.Count > 1)
            return false;

        matches = DirectShape.Matches(text).Cast<Match>().ToList();
        if (matches.Count != 1)
            return false;

        proof = From(matches[0], false);
        return true;
    }

    internal static bool TryApply(
        string fullText,
        SequencePollingProof expected,
        out string rewritten)
    {
        rewritten = fullText;
        var text = Normalize(fullText);
        var regex = expected.WrappedPcall ? PcallShape : DirectShape;
        var matches = regex.Matches(text)
            .Cast<Match>()
            .Where(m =>
                m.Groups["system"].Value.Equals(expected.SystemName, StringComparison.Ordinal) &&
                m.Groups["bridge"].Value.Equals(expected.BridgeVariable, StringComparison.Ordinal) &&
                m.Groups["seq"].Value.Equals(expected.SequenceVariable, StringComparison.Ordinal) &&
                m.Groups["member"].Value.Equals(expected.SequenceMember, StringComparison.Ordinal) &&
                m.Groups["last"].Value.Equals(expected.LastVariable, StringComparison.Ordinal))
            .ToList();

        if (matches.Count != 1)
            return false;

        var m = matches[0];
        var helperName = "__gcetSequenceSystem_" + StableStem(expected.SystemName);
        var indent = m.Groups["indent"].Value;

        var replacement =
            indent + "local " + expected.BridgeVariable + " = " + helperName + "()\n" +
            indent + "if not " + expected.BridgeVariable + " then return end\n" +
            indent + "local " + expected.SequenceVariable + " = " +
            expected.BridgeVariable + "." + expected.SequenceMember + "\n" +
            indent + "if " + expected.SequenceVariable + " == " + expected.LastVariable +
            " then return end\n" +
            indent + expected.LastVariable + " = " + expected.SequenceVariable;

        text =
            text[..m.Index] +
            replacement +
            text[(m.Index + m.Length)..];

        if (!text.Contains(
                "local function " + helperName + "()",
                StringComparison.Ordinal))
        {
            var helper =
                "-- G-CET generic sequence-first ScriptableSystem polling: " +
                expected.SystemName + "\n" +
                "local __gcetSequenceApi_" + StableStem(expected.SystemName) + " = nil\n" +
                "local function " + helperName + "()\n" +
                "    if __gcetSequenceApi_" + StableStem(expected.SystemName) + " == nil then\n" +
                "        local ok, engine = pcall(GetMod, \"0-Engine\")\n" +
                "        if ok and type(engine) == \"table\" then\n" +
                "            __gcetSequenceApi_" + StableStem(expected.SystemName) +
                " = type(engine.GCET) == \"table\" and engine.GCET or engine\n" +
                "        end\n" +
                "    end\n" +
                "    local api = __gcetSequenceApi_" + StableStem(expected.SystemName) + "\n" +
                "    if api and type(api.GetScriptableSystem) == \"function\" then\n" +
                "        return api.GetScriptableSystem(\"" +
                LuaEscape(expected.SystemName) + "\")\n" +
                "    end\n" +
                "    local container = Game.GetScriptableSystemsContainer()\n" +
                "    if not container then return nil end\n" +
                "    return container:Get(CName.new(\"" +
                LuaEscape(expected.SystemName) + "\"))\n" +
                "end\n\n";

            text = helper + text;
        }

        rewritten = text;
        return true;
    }

    private static SequencePollingProof From(Match m, bool wrapped) =>
        new(
            m.Groups["system"].Value,
            m.Groups["bridge"].Value,
            m.Groups["seq"].Value,
            m.Groups["member"].Value,
            m.Groups["last"].Value,
            wrapped);

    private static string StableStem(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }

    private static string LuaEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
