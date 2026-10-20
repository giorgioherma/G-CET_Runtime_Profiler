# Source-first shared getter reuse (Profiler/Resolver CI)

## Contract: GCET_NATIVE_READ_REUSE_V1

This additive feature converts trivial, source-proven zero-argument \`Game.Get...()\`
calls into calls to the **existing 0-Engine shared providers**. It operates across
the installed Lua source tree instead of only measured callback bodies, and has
**no callback CPU floor**. No mod is manually edited: the normal generated
Resolver overlay ZIP contains the rewritten source and required 0-Engine runtime.

**Scope:** The first stage covers only the authorized \`SharedProviderCatalog\`
getters already supported by 0-Engine (including \`Game.GetPlayer()\` and
\`Game.Get...System()\`). Source is scanned outside comments, quoted strings
and Lua long-bracket literals. A Lua file shadowing/rebinding the global
\`Game\` facade is rejected conservatively.

**Exclusions:** No \`GetWorldPosition()\` / \`GetWorldOrientation()\` or arbitrary
entity method is rewritten: RED4ext native snapshot returns XYZ scalars,
not a source-equivalent Vector4 userdata with proven same-frame freshness.
No callback, Observe, game event, scheduler, zone, quest or author wake is
retired by this lane. PASS11 and the other independently proven semantic
native-location passes remain responsible for native engine-work elimination.
Generic source rewrite does not automatically mean a RED4ext lookup is used:
the already-shared 0-Engine provider remains authoritative.

## Source-first Resolver output

New additive \`nativeReadReuse\` object in \`G-CET_Resolver.json\`:

- \`contract: GCET_NATIVE_READ_REUSE_V1\`
- \`producerOwner: 0-Engine\`
- \`files[]\`: \`relativeFile\`, original installed SHA256, explicit proof,
  \`operations[]\` with provider, getter, occurrence count, signature,
  receiver identity, optional backend, fallback and retirement policy.
- \`measuredOutboundCrossingsAvoided = null\` and
  \`measuredEngineGetterWorkAvoided = null\` until independently measured.

The Generator refuses a file with a mismatched source digest; it scans
the **effective staged source**, after semantic injections, to revalidate
each direct read and compose both optimizations in one pass.

## Runtime and automatic integration

1. The Resolver scans all installed CET Lua files without a measured callback
   gate (excluding 0-Engine and the tool's own infrastructure).
2. Only exact authorized getter calls in lexical Lua code qualify.
3. The generated overlay substitutes the read with a shared helper and uses
   the already existing shared 0-Engine provider.
4. If unavailable/unknown, the helper calls the original \`Game.Get...()\`.
5. 0-Engine remains the logical owner; no new native getter producer, Lua frame
   hook, per-mod consumer or manually modified installed source is created.
6. Native snapshot API/version is available independently for later
   phase-correct/player-vector interoperability, but is not silently used
   to substitute an incompatible return type.

The manifest includes \`NATIVE_READ_REUSE\` records with actual replaced
occurrences. Those are **substitutions**, not measured saved calls. A/B
runtime profiling must establish the count of avoided outbound Lua→C++
crossings and distinguish it from actual game-engine work eliminated.

## Verification and safety

CI contract test exercises exact and chained getters, multiple providers,
helper modules not covered by callback measurements, Lua long strings,
ordinary strings, comments, shadowed \`Game\`, framework exclusion,
source hash drift, preservation of existing semantic staged content,
and repeated-generation idempotence. Existing callback, shared-provider,
native location, blackboard and 0-Engine suites remain active.

The compiled code and tests run in the managed Profiler/Resolver workflow;
**no CET native profiler DLL or RED4ext plugin is rebuilt by this feature**.
Optional future profiler extension: bounded aggregate outgoing Lua→C++ getter
counts for A/B. Existing inbound native→Lua marshalling attribution is not
a substitute and is never reported as exact outbound savings.
