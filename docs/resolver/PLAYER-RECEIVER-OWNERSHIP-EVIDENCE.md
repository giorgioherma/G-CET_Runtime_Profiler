# Player receiver ownership evidence — resolver capture contract V1

## Purpose

Classify **callback context** separately from the **receiver of each getter**
without additional runtime capture overhead, without assuming that
\`PlayerPuppet\` callbacks only interact with the local player, and without
mod-specific manual edits.

This is emitted in every normal \`G-CET_Resolver.json\` created by the main
Profiler/Resolver CI. It uses the callback registration metadata already in
\`CET_Resolver_Input.json\` and the **current installed Lua sources** read
during Resolver processing. No change to \`native-profiler-rebuild.yml\`,
the pinned CET instrumentation DLL or the RED4ext Native Fixed Cache is
needed for this source-first evidence stage.

## JSON contract: \`playerOwnershipEvidence\`

- \`contract: GCET_PLAYER_OWNERSHIP_EVIDENCE_V1\`
- \`origin: EXISTING_CAPTURE_CALLBACK_REGISTRATIONS_PLUS_CURRENT_INSTALLED_LUA_SOURCE\`
- \`nativeProfilerInstrumentationAdded: false\`
- \`runtimeReceiverComparisonPerformed: false\`
- \`workloadMetricsAffected: false\`
- \`callbackClassIsReceiverProof: false\`
- \`classificationIsRewriteAuthorization: false\`
- \`callbacks[]\`: every non-infrastructure captured callback, **not merely
  ranked/hot callbacks**; provides registrationId, owner, kind, target,
  callbackContext, sourceFile, sourceResolved, callsPerSecond, and
  \`getterCallsites[]\` (method, receiverExpression, relative source line,
  receiverTag, proof, runtimeObserved, authorizesRewrite).
- \`summary\`: class-context callbacks and source-proven / traceable / unknown
  getter counts.

### Classification semantics

\`PLAYER_PUPPET_CLASS_CONTEXT\` means the callback's registration target is
\`PlayerPuppet\`; it is **not** evidence that every object inside it is the
current player. \`GLOBAL_EVENT_CONTEXT\` means \`onUpdate\`/similar global CET
event registration. Both are metadata, not receiver classifications.

\`CURRENT_PLAYER_SOURCE_PROVEN\`: exact lexical expression
\`Game.GetPlayer():GetWorldPosition()\` (and the analogous zero-argument
orientation/combat methods) with no locally detectable \`Game\` shadowing.
This describes **source origin**, not a sampled runtime entity identity.

\`PLAYER_PUPPET_OBSERVER_RECEIVER\`: an exact, unshadowed direct
\`Observe\`/\`ObserveAfter\`/\`Override\` registration with literal
\`"PlayerPuppet", "Method"\` parameters and an inline Lua function binds its
**first argument** (whatever the identifier, usually \`self\`) to the
registered PlayerPuppet instance. The operation occurs within that function
and the first parameter was not reassigned. This **does not claim equivalence
to \`Game.GetPlayer()\` across every game lifecycle state**.

\`PLAYER_PUPPET_OBSERVER_ALIAS\`: the same registered first parameter reaches
the operation via a straight-line \`local alias = self\` declaration, without
intervening branch, scope boundary or reassignment; analysis-only.

\`CLASS_BOUND_OBSERVER_RECEIVER\`: a first argument bound to a registered
non-PlayerPuppet class. The exact class is included in \`receiverClass\`;
a generic \`ScriptedPuppet\` registration does not imply nonplayer identity.

The source recognizer finds the enclosing registration in the **full installed
Lua file** and matches the profiler's callback kind, class, method, and
source-line window. It explicitly rejects wrapped registrations
(e.g. \`Event.Observe\`), shadowed registration functions, nested Lua functions,
reassigned first parameters, and ambiguous local aliases. The owner of an
unrelated \`targetNpc:GetWorldPosition()\` call is never inferred from the
PlayerPuppet callback context.

\`PLAYER_ALIAS_SOURCE_EVIDENCE\`: a traceable local player binding and alias
chain without reassignment or lexical scope boundary in the available source
span. It remains **investigation evidence only**, not a conclusive cross-path
runtime proof.

\`UNKNOWN\`: unrecognized/dynamic/mixed/out-of-scope receiver; including
\`this:GetWorldPosition()\` without an exact enclosing direct CET registration, NPC references,
reassigned aliases, ambiguous scope and missing source. It is never
silently classified as "not player".

The scanner masks comments, strings and Lua long-bracket literals; it does
not synthesize any missing profiler measurements, infer native call counts,
assign allocation bytes or retire gameplay callbacks. Every per-getter row
has \`runtimeObserved=false\` and \`authorizesRewrite=false\`.

### Important limits and next extension

This source-first version provides useful receiver-origin evidence at **zero
additional in-game instrumentation overhead**, with all processing done
after capture. It does **not** prove the actual runtime entity identity for
arbitrary object methods. A future capture-only native receiver comparison,
using the separate \`native-profiler-rebuild.yml\` workflow, would be warranted
only for unresolved sites where the CET method dispatch actually exposes a
reliable receiver and current-player identity. Such samples must remain
separate from performance counters. A \`PLAYER\`-only sample is never sufficient
to prove that a never-observed NPC path cannot occur.

This data is additive to \`nativeReadReuse\` and
\`sharedProviderOpportunities\`; no existing selection or on-disk manifest
semantics are replaced. \`GetWorldPosition()\` remains analysis-only until
return-type, source proof and frame-freshness contracts are established.
