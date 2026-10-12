using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Reusable, version-tolerant semantic stage recognition. Each stage is proven
// inside its OWN Lua function, never against a whole file or neighboring helper.
// A marker is not evidence that every stage has survived later manual edits.
internal static class SemanticScopedStages
{
    internal readonly record struct FunctionSpan(int Start, int End, string Text);
    internal sealed record Stage(
        string Function,
        string[] Includes,
        string[] Excludes,
        string[][] Before);

    internal static FunctionSpan RequireFunction(string text, string name)
    {
        if (!TryFunction(text, name, out var span))
            throw new InvalidOperationException(
                $"Source no longer proves a unique top-level Lua function: {name}");
        return span;
    }

    internal static bool TryFunction(
        string text, string name, out FunctionSpan span)
    {
        span = default;
        // Only an unindented END terminates this top-level declaration.
        // In-body nested scopes have their own indentation; ambiguity fails
        // closed rather than cutting at a specific NEXT function's name.
        var pattern =
            @"^[ \t]*(?:local[ \t]+)?function[ \t]+" +
            Regex.Escape(name) +
            @"[ \t]*\([^)\r\n]*\)[^\r\n]*(?:\r?\n).*?^end[ \t]*;?[ \t]*(?=\r?$)";
        var matches = Regex.Matches(
            text, pattern,
            RegexOptions.Multiline | RegexOptions.Singleline |
            RegexOptions.CultureInvariant);
        if (matches.Count != 1)
            return false;
        var match = matches[0];
        span = new FunctionSpan(match.Index, match.Index + match.Length, match.Value);
        return true;
    }

    private static string Normalize(string value) =>
        new(value.Where(ch =>
            !char.IsWhiteSpace(ch) && ch != '"' && ch != '\'')
            .Select(char.ToLowerInvariant).ToArray());

    internal static bool ContainsToken(string source, string fragment) =>
        Normalize(source).Contains(Normalize(fragment), StringComparison.Ordinal);

    internal static bool IsComplete(string[] sourceFiles, Stage stage)
    {
        FunctionSpan scope = default;
        var found = 0;
        foreach (var source in sourceFiles)
        {
            if (!TryFunction(source, stage.Function, out var candidate))
                continue;
            scope = candidate;
            if (++found > 1) return false;
        }
        if (found != 1) return false;

        var body = Normalize(scope.Text);
        if (!stage.Includes.All(x => body.Contains(Normalize(x),
                StringComparison.Ordinal)))
            return false;
        if (stage.Excludes.Any(x => body.Contains(Normalize(x),
                StringComparison.Ordinal)))
            return false;
        foreach (var pair in stage.Before)
        {
            if (pair.Length != 2) return false;
            var first = body.IndexOf(Normalize(pair[0]), StringComparison.Ordinal);
            var second = body.IndexOf(Normalize(pair[1]), StringComparison.Ordinal);
            if (first < 0 || second < 0 || first >= second) return false;
        }
        return true;
    }
}
