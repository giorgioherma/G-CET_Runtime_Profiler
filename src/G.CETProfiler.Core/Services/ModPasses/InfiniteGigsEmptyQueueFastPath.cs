namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyInfiniteGigsEmptyQueueFastPath(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local function processBlueLines(delta)",
            "local function processDelayedMu3N(delta)",
            "local function processDeferredAppearances(delta)",
            "local function processDeferredHostileActions(delta)",
            "local function processPendingStorySpawns(delta)",
            "local function processStoryActorProximityBlips()");
        const string marker="-- G-CET semantic:infinitegigs-no-idle-queue-alloc-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("InfiniteGigs zero-allocation empty queue fast paths already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var s=crlf?file.Text.Replace("\r\n","\n",StringComparison.Ordinal):file.Text;
        var replacements=new (string Label,string Old,string New)[] {
            ("BlueLines","local function processBlueLines(delta)\n  local q = Slice.blueLines or {}\n  if #q == 0 then return end",
                "local function processBlueLines(delta)\n  "+marker+"\n  local q = Slice.blueLines\n  if not q or #q == 0 then return end"),
            ("DelayedMu3N","local function processDelayedMu3N(delta)\n  local q = Slice.delayedMu3N or {}\n  if #q == 0 then return end",
                "local function processDelayedMu3N(delta)\n  local q = Slice.delayedMu3N\n  if not q or #q == 0 then return end"),
            ("DeferredAppearances","local function processDeferredAppearances(delta)\n  local q = Slice.pendingAppearances or {}\n  if #q == 0 then return end",
                "local function processDeferredAppearances(delta)\n  local q = Slice.pendingAppearances\n  if not q or #q == 0 then return end"),
            ("DeferredHostileActions","local function processDeferredHostileActions(delta)\n  local q = Slice.pendingHostileActions or {}\n  if #q == 0 then return end",
                "local function processDeferredHostileActions(delta)\n  local q = Slice.pendingHostileActions\n  if not q or #q == 0 then return end"),
            ("PendingStorySpawns","local function processPendingStorySpawns(delta)\n  local q = Slice.pendingStorySpawns or {}\n  if #q == 0 then return end",
                "local function processPendingStorySpawns(delta)\n  local q = Slice.pendingStorySpawns\n  if not q or #q == 0 then return end"),
            ("StoryActorProximityBlips","local function processStoryActorProximityBlips()\n  local rules = Slice.storyActorRules or {}\n  if not next(rules) then return end",
                "local function processStoryActorProximityBlips()\n  local rules = Slice.storyActorRules\n  if not rules or not next(rules) then return end")
        };
        foreach(var patch in replacements) {
            if(s.Split(patch.Old,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("InfiniteGigs "+patch.Label+" idle source anchor changed.");
            s=s.Replace(patch.Old,patch.New,StringComparison.Ordinal);
        }
        context.Write(file,crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s);
        return SemanticInjectionResult.Success(
            "Six per-frame idle paths now avoid transient empty-table allocations; queue processing, dialogue, mission, save restoration and phone lifecycle remain unchanged.");
    }
}
