using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

internal sealed record LuaLocalBudgetResult(
    int MaxActiveLocals,
    int Line,
    string Scope);

internal static class LuaLocalBudgetAnalyzer
{
    internal const int Limit = 200;

    internal static LuaLocalBudgetResult Analyze(byte[] bytes)
    {
        var offset =
            bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF
                ? 3
                : 0;

        return Analyze(Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset));
    }

    internal static LuaLocalBudgetResult Analyze(string source)
    {
        var tokens = Tokenize(source);
        var functions = new Stack<FunctionState>();
        functions.Push(new FunctionState("main chunk", 1));

        var best = new LuaLocalBudgetResult(0, 1, "main chunk");
        UpdateBest(functions.Peek(), 1, ref best);

        for (var i = 0; i < tokens.Count;)
        {
            var token = tokens[i];
            var state = functions.Peek();

            if (token.IsKeyword("local"))
            {
                if (i + 1 < tokens.Count && tokens[i + 1].IsKeyword("function"))
                {
                    AddLocals(state, 1, token.Line, ref best);
                    i = EnterFunction(tokens, i + 1, functions, ref best);
                    continue;
                }

                var j = i + 1;
                var count = 0;
                while (j < tokens.Count && tokens[j].Kind == LuaTokenKind.Identifier)
                {
                    count++;
                    j++;
                    if (j < tokens.Count && tokens[j].Text == ",")
                    {
                        j++;
                        continue;
                    }
                    break;
                }

                if (count > 0)
                    AddLocals(state, count, token.Line, ref best);

                i = Math.Max(j, i + 1);
                continue;
            }

            if (token.IsKeyword("function"))
            {
                i = EnterFunction(tokens, i, functions, ref best);
                continue;
            }

            if (token.IsKeyword("if"))
            {
                state.Blocks.Push(new BlockState(BlockKind.If, false, 0, 0));
                i++;
                continue;
            }

            if (token.IsKeyword("then"))
            {
                if (state.Blocks.TryPeek(out var block) &&
                    block.Kind == BlockKind.If &&
                    !block.Started)
                {
                    block.Started = true;
                }
                i++;
                continue;
            }

            if (token.IsKeyword("elseif"))
            {
                if (state.Blocks.TryPeek(out var block) &&
                    block.Kind == BlockKind.If)
                {
                    CloseBlockLocals(state, block);
                    block.Started = false;
                }
                i++;
                continue;
            }

            if (token.IsKeyword("else"))
            {
                if (state.Blocks.TryPeek(out var block) &&
                    block.Kind == BlockKind.If)
                {
                    CloseBlockLocals(state, block);
                    block.Started = true;
                }
                i++;
                continue;
            }

            if (token.IsKeyword("while"))
            {
                state.Blocks.Push(new BlockState(BlockKind.Loop, false, 0, 0));
                i++;
                continue;
            }

            if (token.IsKeyword("for"))
            {
                var pending = CountForVariables(tokens, i + 1);
                state.Blocks.Push(new BlockState(BlockKind.Loop, false, 0, pending));
                i++;
                continue;
            }

            if (token.IsKeyword("do"))
            {
                if (state.Blocks.TryPeek(out var block) &&
                    block.Kind == BlockKind.Loop &&
                    !block.Started)
                {
                    block.Started = true;
                    if (block.PendingLocals > 0)
                        AddBlockLocals(state, block, block.PendingLocals, token.Line, ref best);
                }
                else
                {
                    state.Blocks.Push(new BlockState(BlockKind.Do, true, 0, 0));
                }
                i++;
                continue;
            }

            if (token.IsKeyword("repeat"))
            {
                state.Blocks.Push(new BlockState(BlockKind.Repeat, true, 0, 0));
                i++;
                continue;
            }

            if (token.IsKeyword("until"))
            {
                if (state.Blocks.TryPeek(out var block) &&
                    block.Kind == BlockKind.Repeat)
                {
                    CloseBlockLocals(state, block);
                    state.Blocks.Pop();
                }
                i++;
                continue;
            }

            if (token.IsKeyword("end"))
            {
                if (state.Blocks.TryPeek(out var block))
                {
                    CloseBlockLocals(state, block);
                    state.Blocks.Pop();
                }
                else if (functions.Count > 1)
                {
                    functions.Pop();
                }

                i++;
                continue;
            }

            i++;
        }

        return best;
    }

    private static int EnterFunction(
        IReadOnlyList<LuaToken> tokens,
        int functionIndex,
        Stack<FunctionState> functions,
        ref LuaLocalBudgetResult best)
    {
        var open = -1;
        var method = false;

        for (var i = functionIndex + 1; i < tokens.Count; i++)
        {
            if (tokens[i].Text == ":")
                method = true;
            if (tokens[i].Text == "(")
            {
                open = i;
                break;
            }

            if (tokens[i].IsKeyword("function") ||
                tokens[i].IsKeyword("end"))
                return functionIndex + 1;
        }

        if (open < 0)
            return functionIndex + 1;

        var close = FindClosingParen(tokens, open);
        if (close < 0)
            return functionIndex + 1;

        var parameters = 0;
        for (var i = open + 1; i < close; i++)
        {
            if (tokens[i].Kind == LuaTokenKind.Identifier)
                parameters++;
        }

        if (method)
            parameters++;

        var state = new FunctionState(
            $"function starting at line {tokens[functionIndex].Line}",
            tokens[functionIndex].Line);
        functions.Push(state);

        if (parameters > 0)
            AddLocals(state, parameters, tokens[functionIndex].Line, ref best);
        else
            UpdateBest(state, tokens[functionIndex].Line, ref best);

        return close + 1;
    }

    private static int FindClosingParen(
        IReadOnlyList<LuaToken> tokens,
        int open)
    {
        var depth = 0;
        for (var i = open; i < tokens.Count; i++)
        {
            if (tokens[i].Text == "(")
                depth++;
            else if (tokens[i].Text == ")")
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }

    private static int CountForVariables(
        IReadOnlyList<LuaToken> tokens,
        int start)
    {
        var count = 0;
        var expectName = true;

        for (var i = start; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Text == "=" || token.IsKeyword("in"))
                break;

            if (expectName && token.Kind == LuaTokenKind.Identifier)
            {
                count++;
                expectName = false;
                continue;
            }

            if (!expectName && token.Text == ",")
            {
                expectName = true;
                continue;
            }

            if (!expectName)
                break;
        }

        return count;
    }

    private static void AddLocals(
        FunctionState state,
        int count,
        int line,
        ref LuaLocalBudgetResult best)
    {
        if (count <= 0)
            return;

        if (state.Blocks.TryPeek(out var block) && block.Started)
        {
            AddBlockLocals(state, block, count, line, ref best);
            return;
        }

        state.ActiveLocals += count;
        UpdateBest(state, line, ref best);
    }

    private static void AddBlockLocals(
        FunctionState state,
        BlockState block,
        int count,
        int line,
        ref LuaLocalBudgetResult best)
    {
        block.DeclaredLocals += count;
        state.ActiveLocals += count;
        UpdateBest(state, line, ref best);
    }

    private static void CloseBlockLocals(
        FunctionState state,
        BlockState block)
    {
        if (block.DeclaredLocals <= 0)
            return;

        state.ActiveLocals -= block.DeclaredLocals;
        block.DeclaredLocals = 0;
    }

    private static void UpdateBest(
        FunctionState state,
        int line,
        ref LuaLocalBudgetResult best)
    {
        if (state.ActiveLocals > best.MaxActiveLocals)
        {
            best = new LuaLocalBudgetResult(
                state.ActiveLocals,
                line,
                state.Label);
        }
    }

    private static List<LuaToken> Tokenize(string source)
    {
        var tokens = new List<LuaToken>();
        var line = 1;

        for (var i = 0; i < source.Length;)
        {
            var ch = source[i];

            if (ch == '\r')
            {
                i++;
                if (i < source.Length && source[i] == '\n')
                    i++;
                line++;
                continue;
            }

            if (ch == '\n')
            {
                line++;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            if (ch == '-' && i + 1 < source.Length && source[i + 1] == '-')
            {
                i += 2;
                if (i < source.Length && source[i] == '[' &&
                    TryLongBracket(source, i, out var contentStart, out var closeStart, out var closeLength))
                {
                    CountNewlines(source, contentStart, closeStart + closeLength, ref line);
                    i = closeStart + closeLength;
                    continue;
                }

                while (i < source.Length && source[i] != '\r' && source[i] != '\n')
                    i++;
                continue;
            }

            if (ch == '\'' || ch == '"')
            {
                var quote = ch;
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\')
                    {
                        i += Math.Min(2, source.Length - i);
                        continue;
                    }

                    if (source[i] == quote)
                    {
                        i++;
                        break;
                    }

                    if (source[i] == '\r' || source[i] == '\n')
                    {
                        if (source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
                            i++;
                        line++;
                    }
                    i++;
                }
                continue;
            }

            if (ch == '[' &&
                TryLongBracket(source, i, out var longStart, out var longClose, out var longCloseLength))
            {
                CountNewlines(source, longStart, longClose + longCloseLength, ref line);
                i = longClose + longCloseLength;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var start = i;
                i++;
                while (i < source.Length &&
                       (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                    i++;

                var text = source[start..i];
                tokens.Add(new LuaToken(
                    text,
                    Keywords.Contains(text)
                        ? LuaTokenKind.Keyword
                        : LuaTokenKind.Identifier,
                    line));
                continue;
            }

            if ("(),=:;".Contains(ch))
            {
                tokens.Add(new LuaToken(ch.ToString(), LuaTokenKind.Symbol, line));
                i++;
                continue;
            }

            i++;
        }

        return tokens;
    }

    private static bool TryLongBracket(
        string source,
        int start,
        out int contentStart,
        out int closeStart,
        out int closeLength)
    {
        contentStart = 0;
        closeStart = 0;
        closeLength = 0;

        if (start >= source.Length || source[start] != '[')
            return false;

        var i = start + 1;
        var equals = 0;
        while (i < source.Length && source[i] == '=')
        {
            equals++;
            i++;
        }

        if (i >= source.Length || source[i] != '[')
            return false;

        contentStart = i + 1;
        var close = "]" + new string('=', equals) + "]";
        closeStart = source.IndexOf(close, contentStart, StringComparison.Ordinal);
        if (closeStart < 0)
        {
            closeStart = source.Length;
            closeLength = 0;
            return true;
        }

        closeLength = close.Length;
        return true;
    }

    private static void CountNewlines(
        string source,
        int start,
        int end,
        ref int line)
    {
        end = Math.Min(end, source.Length);
        for (var i = start; i < end; i++)
        {
            if (source[i] == '\n')
                line++;
        }
    }

    private static readonly HashSet<string> Keywords =
        new(StringComparer.Ordinal)
        {
            "and","break","do","else","elseif","end","false","for","function",
            "goto","if","in","local","nil","not","or","repeat","return","then",
            "true","until","while"
        };

    private sealed class FunctionState
    {
        internal FunctionState(string label, int line)
        {
            Label = label;
            StartLine = line;
        }

        internal string Label { get; }
        internal int StartLine { get; }
        internal int ActiveLocals { get; set; }
        internal Stack<BlockState> Blocks { get; } = new();
    }

    private sealed class BlockState
    {
        internal BlockState(
            BlockKind kind,
            bool started,
            int declaredLocals,
            int pendingLocals)
        {
            Kind = kind;
            Started = started;
            DeclaredLocals = declaredLocals;
            PendingLocals = pendingLocals;
        }

        internal BlockKind Kind { get; }
        internal bool Started { get; set; }
        internal int DeclaredLocals { get; set; }
        internal int PendingLocals { get; }
    }

    private enum BlockKind
    {
        If,
        Loop,
        Do,
        Repeat
    }

    private enum LuaTokenKind
    {
        Identifier,
        Keyword,
        Symbol
    }

    private sealed record LuaToken(
        string Text,
        LuaTokenKind Kind,
        int Line)
    {
        internal bool IsKeyword(string value) =>
            Kind == LuaTokenKind.Keyword &&
            Text.Equals(value, StringComparison.Ordinal);
    }
}
