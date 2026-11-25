using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Read-only first recognition slice. It reports structural proof separately
// from native eligibility: no source rewrite until exact individual Context
// binding and a matching native capability exist. No event/mod/state names.
internal static class GenericObserverGuardSource
{
    private static readonly Regex Lex = new(
        """(?<comment>--\[(?<ce>=*)\[.*?\]\k<ce>\]|--[^\r\n]*)|(?<long>\[(?<le>=*)\[.*?\]\k<le>\])|(?<quoted>"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*')|(?<id>[A-Za-z_][A-Za-z0-9_]*)|\S""",
        RegexOptions.Singleline|RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Keywords=new(StringComparer.Ordinal)
    {"and","break","do","else","elseif","end","false","for","function","goto","if","in","local","nil","not","or","repeat","return","then","true","until","while"};
    private static readonly HashSet<string> Continuation=new(StringComparer.Ordinal)
    {",",".",":","[","(", "{","+","-","*","/","%","^","<",">","=","~","and","or"};
    private static bool At(Match[] t,int i,params string[] values) =>
        i>=0&&i+values.Length<=t.Length&&t.Skip(i).Take(values.Length).Select(x=>x.Value).SequenceEqual(values);
    private static bool Name(Match t)=>t.Groups["id"].Success&&!Keywords.Contains(t.Value);
    private static bool Literal(Match t)=>t.Value is "true" or "false" or "nil";
    private static bool DirectString(Match t)=>t.Groups["quoted"].Success&&!t.Value.Contains('\\')&&t.Length>2;
    private static bool Observer(string v)=>v is "Observe" or "ObserveBefore" or "ObserveAfter" or "Override";
    internal sealed record Write(int Start,int End,string Value);
    internal sealed record Site(int Offset,string Kind,string Class,string Method,string? State,
        bool ActiveWhenTruthy,string Reason,int DeclarationEnd,IReadOnlyList<Write> Writes)
    {
        internal bool SourceProven=>Reason=="SIMPLE_LOCAL_PROVEN";
    }
    // The restricted lexical subset deliberately refuses shadowing and
    // unsupported statement shapes instead of guessing symbol ownership.
    // Depth is only used to prove a single declaration in the main chunk;
    // any unbalanced block/delimiter refuses all suppression in this file.
    private static bool Depths(Match[] t,out int[] depth)
    {
        depth=new int[t.Length];var blocks=new Stack<string>();
        int round=0,square=0,curly=0;
        for(int i=0;i<t.Length;++i)
        {
            var v=t[i].Value;
            depth[i]=blocks.Count+round+square+curly;
            switch(v)
            {
                case "function":case "if":case "repeat":blocks.Push(v);break;
                case "for":case "while":blocks.Push("loop-await-do");break;
                case "do":
                    if(blocks.TryPeek(out var b)&&b=="loop-await-do"){blocks.Pop();blocks.Push("loop");}
                    else blocks.Push("do");
                    break;
                case "end":
                    if(!blocks.TryPop(out var e)||e is "repeat" or "loop-await-do")return false;
                    break;
                case "until":
                    if(!blocks.TryPop(out var r)||r!="repeat")return false;break;
                case "(":round++;break;case ")":round--;break;
                case "[":square++;break;case "]":square--;break;
                case "{":curly++;break;case "}":curly--;break;
            }
            if(round<0||square<0||curly<0)return false;
        }
        return blocks.Count==0&&round==0&&square==0&&curly==0;
    }
    private static bool StartsLine(string s,Match token)
    {
        int start=s.LastIndexOf('\n',Math.Max(0,token.Index-1))+1;
        return string.IsNullOrWhiteSpace(s[start..token.Index].TrimStart('\uFEFF'));
    }
    private static bool EndsStatement(string source,Match[] t,int last)
    {
        if(last+1==t.Length)return true;
        var next=t[last+1];
        if(next.Value==";")return true;
        if(Continuation.Contains(next.Value)||next.Groups["quoted"].Success||next.Groups["long"].Success)return false;
        // Newline is not a Lua expression terminator by itself. The next
        // token must also be incapable of continuing this literal RHS.
        return source.AsSpan(t[last].Index+t[last].Length,next.Index-t[last].Index-t[last].Length).Contains('\n');
    }
    private static bool SimpleWrite(string s,Match[] t,int i)
    {
        if(i+2>=t.Length||!At(t,i+1,"=")||!Literal(t[i+2])||
            !StartsLine(s,t[i])||!EndsStatement(s,t,i+2))return false;
        if(i==0)return false;
        return t[i-1].Value is not ("local" or "for" or "function" or "," or "." or ":" or "{" or "[");
    }
    private static bool SimpleRead(Match[] t,int i)
    {
        if(i<1||i+1>=t.Length)return false;
        var before=i-1;
        if(t[before].Value=="not")before--;
        if(before<0)return false;
        return (t[before].Value is "if" or "elseif")&&t[i+1].Value=="then" ||
               t[before].Value=="while"&&t[i+1].Value=="do";
    }
    internal static IReadOnlyList<Site> Analyze(string source)
    {
        var t=Lex.Matches(source).Cast<Match>().Where(m=>!m.Groups["comment"].Success).ToArray();
        bool balanced=Depths(t,out var depth);
        var result=new List<Site>();
        // Official CET sandbox does not expose debug/upvalue or FFI APIs.
        // If the source explicitly uses reflective/dynamic facilities,
        // refuse this subset; do not assume their implementation is benign.
        bool reflection=t.Any(x=>x.Groups["id"].Success&&
            x.Value is "debug" or "getupvalue" or "setupvalue" or "upvaluejoin" or "getfenv" or "setfenv" or "_G" or "_ENV" or "ffi" or "load" or "loadstring" or "loadfile" or "dofile");
        // Never mistake an observer alias/rebinding for the CET global API.
        // Any non-direct use of these names makes this file ambiguous.
        bool apiAlias=Enumerable.Range(0,t.Length).Any(i=>Observer(t[i].Value)&&
            (!At(t,i+1,"(")||(i>0&&t[i-1].Value is "." or ":" or "function" or "local")));
        for(int i=0;i+5<t.Length;++i)
        {
            if(!Observer(t[i].Value)||!At(t,i+1,"(")||!DirectString(t[i+2])||
                !At(t,i+3,",")||!DirectString(t[i+4])||!At(t,i+5,","))continue;
            string kind=t[i].Value,klass=t[i+2].Value[1..^1],method=t[i+4].Value[1..^1];
            string? state=null;bool truthy=false;
            Site Refuse(string why)=>new(t[i].Index,kind,klass,method,state,truthy,why,-1,Array.Empty<Write>());
            if(kind=="Override"){result.Add(Refuse("OVERRIDE_RETAINED"));continue;}
            if(!At(t,i+6,"function","(")){result.Add(Refuse("NON_INLINE_CALLBACK"));continue;}
            int p=i+8;bool paramsOk=true;
            while(p<t.Length&&t[p].Value!=")")
            {
                // Parameters and varargs are not state ownership evidence.
                if(!Name(t[p])&&t[p].Value is not ("," or ".")){paramsOk=false;break;}p++;
            }
            if(!paramsOk||p>=t.Length){result.Add(Refuse("UNPROVEN_PARAMETERS"));continue;}
            int guard=p+1;if(At(t,guard,";"))guard++;
            if(!At(t,guard,"if")){result.Add(Refuse("NO_LEADING_GUARD"));continue;}
            int name=guard+1;truthy=At(t,name,"not");if(truthy)name++;
            if(name>=t.Length||!Name(t[name])||!At(t,name+1,"then","return","end")&&
                !At(t,name+1,"then","return",";","end"))
            {result.Add(Refuse("COMPLEX_OR_NONEMPTY_RETURN"));continue;}
            state=t[name].Value;
            if(!balanced){result.Add(Refuse("UNBALANCED_SOURCE"));continue;}
            if(reflection){result.Add(Refuse("REFLECTION_OR_DYNAMIC_ENVIRONMENT"));continue;}
            if(apiAlias){result.Add(Refuse("OBSERVER_API_ALIAS_OR_REBIND"));continue;}
            if(t.Skip(i+8).Take(p-(i+8)).Any(x=>x.Value==state))
            {result.Add(Refuse("CALLBACK_PARAMETER_SHADOW"));continue;}
            var uses=Enumerable.Range(0,t.Length).Where(j=>t[j].Value==state&&t[j].Groups["id"].Success).ToArray();
            var declarations=uses.Where(j=>j>0&&depth[j-1]==0&&At(t,j-1,"local",state,"=")&&
                j+2<t.Length&&Literal(t[j+2])&&StartsLine(source,t[j-1])&&EndsStatement(source,t,j+2)).ToArray();
            if(declarations.Length!=1||declarations[0]>=i)
            {result.Add(Refuse("NO_UNIQUE_CHUNK_LITERAL_LOCAL"));continue;}
            int d=declarations[0];var writes=new List<Write>();bool safe=true;
            foreach(int j in uses)
            {
                if(j==d)continue;
                if(j<d){safe=false;break;}
                if(SimpleRead(t,j))continue;
                if(SimpleWrite(source,t,j))
                {
                    // Only proven standalone literal assignment. For a
                    // future emitter, transition at the exact RHS endpoint,
                    // not by replacing the author assignment or its order.
                    writes.Add(new(t[j].Index,t[j+2].Index+t[j+2].Length,t[j+2].Value));
                    continue;
                }
                safe=false;break;
            }
            if(!safe){result.Add(Refuse("UNPROVEN_USE_SHADOW_OR_WRITE"));continue;}
            result.Add(new(t[i].Index,kind,klass,method,state,truthy,
                kind=="ObserveAfter"?"AFTER_SOURCE_PROVEN_NATIVE_PENDING":"SIMPLE_LOCAL_PROVEN",
                t[d+2].Index+t[d+2].Length,writes));
        }
        return result;
    }
}
