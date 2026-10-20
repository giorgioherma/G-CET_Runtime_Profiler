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

## Deterministic generation order (source graph, not mod list)

1. **GENERIC 1 — callbacks:** source-verified action routing, override
   prefilters, frame consolidation and mechanically safe generic recipes.
2. **GENERIC 2 — shared infrastructure and getters:** 0-Engine PSM,
   SpatialHash and Lifecycle stages, native forced-live player vectors,
   and broad source-first shared getter reuse. Each consumes previously
   staged bytes and finishes BEFORE source-specific semantic injection.
3. **SEMANTIC — behavior:** version-fluid, source-structural mod-specific
   scheduling, wake state, lifecycle and dormancy stages over final generic
   source. Reuse existing capabilities instead of requiring old template text.
4. **SEMANTIC native-location substage — last:** apply remaining compiled
   location subscriptions and safe same-generation follow-ups for Poker,
   Tunnel, Illegal Mechanic, Dedka, Card Collection, MarmurBank, Yaiba,
   and Ziggy. No generic source rewriting occurs after semantic.
5. **FINALIZE:** compose additive 0-Engine host files; compile native
   positions from CURRENT installed/staged owner source; verify native DLL
   availability; check every generated Lua file against the 200 active-local
   hard limit; package one coherent overlay ZIP and manifest.

A local-stage refusal must neither revert independent changes nor be
reported as a completed capability. Runtime native API availability is
different from generation-time location composition.

### Add/remove/update/reset contract

An added mod becomes eligible from its installed source, not a historical
mod-list snapshot. Missing measurement is not permission for arbitrary CPU
based generic rewrites; source-proven eligible stages remain independent.
A removed mod must not acquire new Lua output or retained native anchors.
Updated, reset or partially optimized mods are evaluated per stage, never
gated on source-file SHA or release version. Repeated generation must not
duplicate listeners or wipe previous manual optimizations.

When native service is included, the current source-derived complete GCAS
manifest (including already-satisfied owners with zero new Lua edits)
replaces the previous manifest; removed owners must disappear. Static owner
geometry alone does not establish that its subscription hook succeeded.

## Regression matrix

Test original, optimized, eight-reset/mixed, updated, newly added, removed,
and repeated sources; preserve all unrelated files. Assert generic phases
precede semantic/native phases, native-location subscription closure,
owner-count correctness, local-variable ceiling, DLL fallback behavior,
and stage-by-stage generated ZIP postconditions.

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
