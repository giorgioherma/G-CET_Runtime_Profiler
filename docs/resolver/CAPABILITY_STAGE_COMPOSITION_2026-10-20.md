# Resolver capability-stage composition — 2026-10-20

## User baseline and non-negotiable integration contract
All PASS0–PASS11 source optimizations are independently valuable. Never
request wholesale reinstallation of original mods simply to regenerate a
later implementation. The Resolver must operate on original, previously
optimized, partly optimized, manually edited, and updated source graphs.

A **mod is not a stage**. Each optimization must be classified separately:
**SOURCE_SATISFIED**, **SAFE_TO_APPLY**, or **REFUSED_WITH_REASON**.
A refused neighbor must not erase or veto another stage. Source postconditions,
owner identity, callback lifecycle, and native API capabilities—not mod
version, source SHA, or comments alone—govern adoption. A file digest is only
provenance and within-run source snapshot consistency (and profiler ASI
original-state restoration remains a different problem).

## Existing deterministic implementation order in PassGeneratorService
1. Source-verified generic callback transformations, staged in memory from
   the current deployed Lua.
2. Source-proven semantic library stages, with same-pass native-location
   follow-up for prerequisites materialized by an earlier stage.
3. 0-Engine PSM 12-definition-key snapshot in BlackboardCache2.lua;
   recognizes already-complete cache before attempting insertion.
4. SpatialHash poll-cell-radius and grid-row-X stages (independent
   substage completion) and separate Lifecycle same-update entity ID reuse.
   Consume previously staged bytes, not just disk source.
5. Exact current-local-player **direct** Game.GetPlayer():GetWorldPosition()
   source rewrites through the native **forced-live typed Vector4** capability
   with original fallback. Scan current AND earlier staged files; add new
   callsites without duplicating a prior helper or changing its fallback.
6. Authorized shared 0-Engine getter reuse after semantic/native-position
   stages. Scan current and staged sources, reuse source-proven existing
   helpers, and do not rewrite generated helper fallbacks.
7. Additive, source-preserving 0-Engine workload and location host services.
   If a customized native host lacks attachCompiled and safe immutable-anchor
   teardown, add these capabilities; do not replace other custom modules.
8. Compile the source-backed GCAS location manifest, including owners
   already completely optimized in installed Lua with **zero** new owner
   edits. Compile before deciding whether the native DLL is required.
9. Package one complete ZIP with native DLL + location manifest whenever
   supported static/native callsites require it. Source hashes in the ZIP
   describe the exact original input, not mod-version eligibility.

This order is a dependency model, not a strict file-level exclusion model.
A stage may skip itself without preventing later, independent stages.

## PASS11 native-location upgrade
Old PASS11 Poker, Tunnel, Dedka and Illegal Mechanic used dynamic
`NativeLocationHost:attach` rather than source-compiled anchors.
Source-proven adapters upgrade compatible owners to `attachCompiled`.
Illegal Mechanic's old aggregate `10033` owner must be removed only after
proving the old registration plus global FAR gate. The new registry-derived
owners use `1003301 + index` per mechanic and preserve author-defined NPC
75m spawn / 95m despawn, active/pending cleanup, and prior player-position
reuse. A changed discovery shape must be measured from installed
REDscript data, never guessed from a fixed release template.

Location geometry and callback behavior are **separate proofs**; unchanged
geometry can still be used even when an unrelated listener rewrite is refused.
An already optimized location owner must remain in the compiled GCAS manifest.

## Native player-state boundary
The current typed Vector4 native API supports strict live reads
`GCETPlayerVector4Live` with immediate `GCETPlayerVector4Valid` and
fallback. An arbitrary `player`, `self`, actor or NPC receiver is **not**
automatically equivalent to the local player. Such callsites remain
independently unoptimized unless receiver identity, return type, call timing,
validity, and lifetime are proven. Frame-shared position reuse is not
implicitly authorized by the existence of a DLL. PSM Blackboard definitions
are a distinct source-snapshot optimization and are not the same as a
native PlayerPuppet state cache.

## Regression matrix
Exercise (a) original source, (b) PASS0 collapsed optimized source, (c)
each incremental PASS1–PASS11, (d) combinations where only part of a
semantic was installed, (e) updated author source preserving behavior,
(f) already-complete no-op second pass, (g) current player getter newly
introduced in an earlier staged transform, and (h) missing DLL/API or
unknown host source. Validate generated ZIP, transform + omission manifest,
gameplay fallback, compiled owner count, and native session reload.

CI contracts are valuable but are NOT proof of all source mixtures or
real gameplay behavior. A green release is installation-eligible only
for demonstrated source variants; other variants fail closed per stage,
not globally. No promise is made that every arbitrary future mod update
can be rewritten safely.
