param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$dll=Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'
if (!(Test-Path $dll)) { throw 'Missing published core DLL' }
[void][Reflection.Assembly]::LoadFrom($dll)

# Explicitly distinct units: two ready callback rows from the SAME owner
# are one mod/one semantic rule, while a no-capture candidate is a separate
# source-proven mod. A callback ALREADY_SATISFIED is not an optimized mod.
$report=@'
{
 "callbackFamilies":[
  {"topConsumers":[
   {"owner":"QuestA","registrationId":11,"disposition":"ALREADY_SATISFIED",
    "generic":{"Automatable":false,"RecipeFamilies":[]},
    "semantic":{"Matched":true,"SourceProofSatisfied":true,"AlreadySatisfied":false,
      "generationReady":true,"RuleId":"quest-v2","PolicyClass":"QUEST_LUA"}},
   {"owner":"QuestA","registrationId":12,"disposition":"ALREADY_SATISFIED",
    "generic":{"Automatable":false,"RecipeFamilies":[]},
    "semantic":{"Matched":true,"SourceProofSatisfied":true,"AlreadySatisfied":false,
      "generationReady":true,"RuleId":"quest-v2","PolicyClass":"QUEST_LUA"}},
   {"owner":"GigB","registrationId":13,"disposition":"ALREADY_SATISFIED",
    "generic":{"Automatable":false,"RecipeFamilies":[]},
    "semantic":{"Matched":true,"SourceProofSatisfied":true,"AlreadySatisfied":true,
      "generationReady":false,"RuleId":"gig-a","PolicyClass":"INVOCATION_LOCAL_NATIVE_PROVIDER_REUSE"}},
   {"owner":"GigB","registrationId":14,"disposition":"ALREADY_SATISFIED",
    "generic":{"Automatable":false,"RecipeFamilies":[]},
    "semantic":{"Matched":true,"SourceProofSatisfied":true,"AlreadySatisfied":true,
      "generationReady":false,"RuleId":"gig-a","PolicyClass":"INVOCATION_LOCAL_NATIVE_PROVIDER_REUSE"}},
   {"owner":"GigB","registrationId":15,"disposition":"ALREADY_SATISFIED",
    "generic":{"Automatable":false,"RecipeFamilies":[]},
    "semantic":{"Matched":true,"SourceProofSatisfied":true,"AlreadySatisfied":true,
      "generationReady":false,"RuleId":"gig-b","PolicyClass":"ACTIVE_STATE_GATE"}},
   {"owner":"GenericC","registrationId":16,"disposition":"GENERIC_PATTERN",
    "generic":{"Automatable":true,"RecipeFamilies":["FRAME_DISPATCH_CONSOLIDATION","UI_VISIBILITY_DORMANCY"]},
    "semantic":{"Matched":false}},
   {"owner":"GenericC","registrationId":17,"disposition":"GENERIC_PATTERN",
    "generic":{"Automatable":true,"RecipeFamilies":["UI_VISIBILITY_DORMANCY"]},
    "semantic":{"Matched":false}},
   {"owner":"QuestD","registrationId":18,"disposition":"UNRESOLVED",
    "materiality":{"material":true},"generic":{"Automatable":false},"semantic":{"Matched":false}},
   {"owner":"QuestD","registrationId":19,"disposition":"UNRESOLVED",
    "materiality":{"material":false},"generic":{"Automatable":false},"semantic":{"Matched":false}}
  ]}
 ],
 "semanticSourceOnlyCandidates":[
  {"owner":"AutoLoot","ruleId":"autoloot","policyClass":"EXACT_ACTION_ROUTING",
   "sourceProofSatisfied":true,"generationEnabled":true,"alreadySatisfied":false},
  {"owner":"UnsafeGuess","ruleId":"bad","policyClass":"EXACT_ACTION_ROUTING",
   "sourceProofSatisfied":false,"generationEnabled":true,"alreadySatisfied":false}
 ]
}
'@
$reportDoc=[System.Text.Json.JsonDocument]::Parse($report)
try {
    $count=[GCETRuntimeProfiler.Core.Services.OptimizationClassCountService]::Analyze($reportDoc.RootElement)
    if ($count.MeasuredModCount -ne 4 -or $count.ReadyModCount -ne 2 -or
        $count.CapturedReadyModCount -ne 1 -or $count.SourceOnlyReadyModCount -ne 1 -or
        $count.ReadyRuleCount -ne 2 -or $count.AlreadySourceProvenModCount -ne 1 -or
        $count.AlreadySourceProvenRuleCount -ne 2 -or $count.GenericReadyModCount -ne 1 -or
        $count.MaterialRemainingModCount -ne 1 -or $count.BelowThresholdModCount -ne 1 -or
        $count.AlreadySatisfiedCallbackCount -ne 5) {
        throw "Resolver coverage miscount: $($count | ConvertTo-Json -Depth 7 -Compress)"
    }
    $quest=@($count.ReadySemanticClasses | Where-Object {$_.Class -eq 'QUEST_LUA'})
    $native=@($count.AlreadySemanticClasses | Where-Object {$_.Class -eq 'INVOCATION_LOCAL_NATIVE_PROVIDER_REUSE'})
    $ui=@($count.GenericReadyClasses | Where-Object {$_.Class -eq 'UI_VISIBILITY_DORMANCY'})
    if ($quest.Count -ne 1 -or $quest[0].ModCount -ne 1 -or $quest[0].RuleCount -ne 1 -or
        $native.Count -ne 1 -or $native[0].ModCount -ne 1 -or
        $ui.Count -ne 1 -or $ui[0].ModCount -ne 1) {
        throw 'Duplicate callback rows inflated per-class mod counts'
    }
} finally { $reportDoc.Dispose() }

# Two modified files are still ONE semantic rule in ONE mod. Zero-Engine
# support files are not actual optimized mods. Different owners sharing a
# class count as multiple mods, and generic classes remain separate.
$manifest=@'
{
 "transforms":[
  {"Owner":"QuestA","RuleId":"quest-v2","PolicyClass":"QUEST_LUA",
   "type":"SEMANTIC_RULE","admissionEvidence":"CAPTURE_MATCHED_SOURCE_PROVEN",
   "files":["QuestA/init.lua","QuestA/nav.lua"]},
  {"Owner":"QuestB","RuleId":"quest-v3","PolicyClass":"QUEST_LUA",
   "type":"SEMANTIC_RULE","admissionEvidence":"CAPTURE_MATCHED_SOURCE_PROVEN",
   "files":["QuestB/init.lua"]},
  {"Owner":"AutoLoot","RuleId":"autoloot","PolicyClass":"EXACT_ACTION_ROUTING",
   "type":"SEMANTIC_RULE","admissionEvidence":"SOURCE_PROVEN_NO_CAPTURE",
   "files":["AutoLoot/init.lua"]},
  {"owner":"GenericC","type":"FRAME_DISPATCH_CONSOLIDATION",
   "registrationId":16,"file":"GenericC/init.lua"}
 ],
 "files":[
  {"path":"0-Engine/init.lua","fixedInfrastructure":true},
  {"path":"0-Engine/modules/Scheduler.lua","fixedInfrastructure":true},
  {"path":"QuestA/init.lua","fixedInfrastructure":false},
  {"path":"QuestA/nav.lua","fixedInfrastructure":false},
  {"path":"QuestB/init.lua","fixedInfrastructure":false},
  {"path":"AutoLoot/init.lua","fixedInfrastructure":false},
  {"path":"GenericC/init.lua","fixedInfrastructure":false}
 ]
}
'@
$manifestDoc=[System.Text.Json.JsonDocument]::Parse($manifest)
try {
    $applied=[GCETRuntimeProfiler.Core.Services.OptimizationClassCountService]::FromManifest($manifestDoc.RootElement)
    if ($applied.AppliedModCount -ne 4 -or $applied.AppliedRuleCount -ne 4 -or
        $applied.ModifiedModFileCount -ne 5 -or $applied.FixedRuntimeFileCount -ne 2 -or
        $applied.SourceOnlyAppliedModCount -ne 1) {
        throw "Pass counts misreported: $($applied | ConvertTo-Json -Depth 7 -Compress)"
    }
    $quest=@($applied.AppliedSemanticClasses | Where-Object {$_.Class -eq 'QUEST_LUA'})
    if ($quest.Count -ne 1 -or $quest[0].ModCount -ne 2 -or $quest[0].RuleCount -ne 2) {
        throw 'Actual pass class coverage did not group by distinct mods'
    }
    $pretty=[GCETRuntimeProfiler.Core.Services.OptimizationClassCountService]::FormatApplied($applied)
    if (!$pretty.Contains('QuestA, QuestB') -or
        !$pretty.Contains('5; fixed runtime infrastructure: 2')) {
        throw 'Actual pass UI is missing independently auditable class names and mod counts'
    }
} finally { $manifestDoc.Dispose() }
Write-Host 'Optimization class coverage PASS: captured and source-only mod readiness, distinct source-verified class counts, applied ZIP owner counts, multi-file and callback de-duplication, fixed 0-Engine exclusion.'
