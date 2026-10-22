using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// EXPLICIT USER OPT-IN. The default public Resolver never removes languages.
/// Once opted in, delete only the reviewed Hotscenes translation/export
/// path, leaving all English defaults and authored scene behavior intact.
/// </summary>
internal static class HotscenesEnglishOnlySource
{
    internal const string Marker="-- G-CET personal:hotscenes-english-only-v1";
    private const string OptionFile="G-CET-UserOptions.json";
    private static readonly RegexOptions Multiline =
        RegexOptions.Multiline|RegexOptions.Singleline|RegexOptions.CultureInvariant;

    internal static bool IsOptedIn(string modsRoot)
    {
        var file=Path.Combine(modsRoot,OptionFile);
        if(!File.Exists(file))return false;
        using var json=JsonDocument.Parse(File.ReadAllText(file));
        return json.RootElement.ValueKind==JsonValueKind.Object &&
            json.RootElement.TryGetProperty("hotscenesEnglishOnly",out var value) &&
            value.ValueKind==JsonValueKind.True;
    }

    internal static int TryStage(string modsRoot,Dictionary<string,byte[]> staged,
        Dictionary<string,int> counts,List<object> transforms,List<object> skipped)
    {
        if(!IsOptedIn(modsRoot))return 0;
        const string relative="hotscenes/init.lua";
        var path=Path.Combine(modsRoot,"hotscenes","init.lua");
        if(!File.Exists(path))return 0;
        var bytes=staged.TryGetValue(relative,out var existing)
            ?existing:File.ReadAllBytes(path);
        var source=NativeReadReuseSource.Decode(bytes);
        try
        {
            if(!TryTransform(source,out var output))
                return 0;
            if(LuaLocalBudgetAnalyzer.Analyze(output).MaxActiveLocals>
                LuaLocalBudgetAnalyzer.Limit)
                throw new InvalidOperationException(
                    "English-only source exceeds the Lua 200-local safety ceiling.");
            if(source.Contains("\r\n",StringComparison.Ordinal))
                output=output.Replace("\n","\r\n",StringComparison.Ordinal);
            var encoded=Encoding.UTF8.GetBytes(output);
            if(bytes.Length>=3 && bytes[0]==0xEF && bytes[1]==0xBB && bytes[2]==0xBF)
                encoded=[0xEF,0xBB,0xBF,..encoded];
            staged[relative]=encoded;
            counts[relative]=counts.GetValueOrDefault(relative)+1;
            transforms.Add(new {type="PERSONAL_HOTSCENES_ENGLISH_ONLY",
                file=relative,optIn=OptionFile,englishDefaultsRetained=true,
                previousActiveLocals=LuaLocalBudgetAnalyzer.Analyze(source).MaxActiveLocals,
                newActiveLocals=LuaLocalBudgetAnalyzer.Analyze(output).MaxActiveLocals,
                localizationFeatureRemoved=true});
            return 1;
        }
        catch(InvalidOperationException exception)
        {
            skipped.Add(new {type="PERSONAL_HOTSCENES_ENGLISH_ONLY",
                category="GENERATION_FAILURE",file=relative,
                reason=exception.Message});
            return 0;
        }
    }

    internal static bool TryTransform(string input,out string output)
    {
        output=input;
        if(input.Contains(Marker,StringComparison.Ordinal))
        {
            if(input.Contains("function loadAllLanguageFiles()",StringComparison.Ordinal) ||
               input.Contains("function getGameUILanguage(",StringComparison.Ordinal) ||
               input.Contains("function loadLocalizedStrings(",StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Previously opted-in English-only Hotscenes source is incomplete.");
            return false;
        }
        var source=input.Replace("\r\n","\n",StringComparison.Ordinal);
        static string Replace(string source,string pattern,string replacement,
            string identity,int expected=1)
        {
            var regex=new Regex(pattern,Multiline);
            var matches=regex.Matches(source);
            if(matches.Count!=expected)
                throw new InvalidOperationException(
                    "Hotscenes English-only source drift at "+identity+
                    ": expected "+expected+" anchor(s), observed "+matches.Count+".");
            return regex.Replace(source,_=>replacement);
        }
        source=Replace(source,
            @"^function saveTextFile\(text, fileName\)\n.*?^end\n\n",
            "","author translation text export");
        source=Replace(source,
            @"^exportedStringsFileHeader = \{\n.*?^\}\n\nstringsFileSet = .*?\n\n",
            "","translation export header/file sets");
        source=Replace(source,
            "^exportTable = \\{\\}\\n.*?^if type\\(jsonDump\\) == 'string' then "+
            "saveTextFile\\(jsonDump, \"language/en-us_template/cetUiStrings.json\"\\) end\\n\\n",
            "","CET language template writer");
        source=Replace(source,
            @"^function replaceTableEntries\(sourceTable, replacementTable, finalDataType, verbose\)\n.*?^end\n\n\n",
            "","language replacement engine");
        source=Replace(source,
            "^local currentUiLanguage = \"en-us\"\\nlanguages = \\{\\n.*?^loadAllLanguageFiles\\(\\)\\n\\n",
            "","language catalog and file scanner");
        source=Replace(source,
            @"^function quickloadUserSettings\(\)\n.*?^end\n\nfunction getGameUILanguage\(updateUserSettings, forceSettingsUpdate\)\n.*?^end\n\nfunction loadLocalizedStrings\(updateUserSettings, forceSettingsUpdate\)\n.*?^end\n\nloadLocalizedStrings\(\)\n\n",
            "","language selection and refresh functions");
        source=Replace(source,
            "^\\t\\tlocal exportTable = \\{\\}\\n\\t\\texportTable\\.exportOrder = "+
            "\\{'fileHeader', 'uiStrings'\\}\\n.*?^\\t\\tend\\n\\tend\\n"+
            "\\tlocal nativeSettings = GetMod\\(\"nativeSettings\"\\)",
            "\tend\n\tlocal nativeSettings = GetMod(\"nativeSettings\")",
            "NativeUI language-template exports");
        source=Replace(source,
            @"^\tlocal lastlanguageSelected = userSettings\.lastLanguageSelected\n\tloadLocalizedStrings\(true, true\)\n\tif userSettings\.lastLanguageSelected ~= lastlanguageSelected then saveUserSettings\(\) end",
            "\tuiStrings = cloneTable(uiDefaultStrings)\n"+
            "\tif nativeUI.updateUiStrings then nativeUI.updateUiStrings() end",
            "NativeUI initialization with English defaults");
        source=Replace(source,
            @"^\t\tlocal lastlanguageSelected = userSettings\.lastLanguageSelected\n\t\tloadLocalizedStrings\(true\)\n\t\tif userSettings\.lastLanguageSelected ~= lastlanguageSelected then saveUserSettings\(\) end\n",
            "","obsolete game-load language refresh",2);
        source=Replace(source,
            @"performerFullNameTranslations\[currentUiLanguage\]",
            "performerFullNameTranslations[\"en-us\"]",
            "performer translation lookup");
        source=Replace(source,
            @"^[ \t]*if type\(settings\.lastLanguageSelected\) == 'string' then userSettings\.lastLanguageSelected = settings\.lastLanguageSelected else userSettings\.lastLanguageSelected = ""en-us"" end\n",
            "","language setting restoration");
        source=Replace(source,
            @"^local function vectorDistance2D\(src, target\);\n.*?^end;\n",
            "","unreferenced 2D distance helper");
        source=Replace(source,@"^local isFemaleScene = false\n",
            "","unreferenced local scene flag");
        foreach(var removed in new[]{"saveTextFile","loadAllLanguageFiles",
            "loadLocalizedStrings","getGameUILanguage","currentUiLanguage",
            "exportedStringsFileHeader","stringsFileSet","replaceTableEntries",
            "function quickloadUserSettings","language/en-us_template"})
            if(source.Contains(removed,StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Hotscenes English-only still references removed feature: "+removed);
        output=Marker+"\n"+source;
        return true;
    }
}
