# G-CET — mandatory startup context

**First read:** this file → [BUILD-PIPELINE.md](BUILD-PIPELINE.md) → [documentation index](../README.md). This is a stable entrypoint, **not a snapshot of any release**. Do not put versions, exact native hashes, CI run numbers, dated captures or active incidents here.

## Project identity

- Repository: `giorgioherma/G-CET_Runtime_Profiler`; development branch: `dev/resolver-profiler-handoff`. Always query current GitHub HEAD and the current user request.
- **Profiler:** standalone Windows GUI/CLI around a transactionally installed/restored CET native DLL. Captures callback CPU, deep source/callsite data, bursts, LuaJIT G-GC allocations/collector phases and optional CapFrameX frametimes; collects raw CSV, HTML and JSON reports.
- **Resolver:** a separate portable app that reads captured evidence plus the user's **current original/live CET mod files read-only**. Generates composable safe generic then source-proven semantic optimization overlays; does not silently edit live mods.
- One Profiler, one Resolver. The internal Resolver decision-layer codename stays **internal** and must not appear in player-facing UI, reports, releases or installer text.

## Invariants

1. Preserve source-mod gameplay, quest state, pins, UI/input, dialogue, activity, transitions, deadlines, lifecycle, cleanup and fallback. A generic/semantic pass must be version-fluid, compose with already-applied/partial changes and be idempotent. See [Resolver contract](../resolver/PROJECT-CONTRACT.md).
   - **Version-fluid is mandatory, not aspirational:** test every new source injector against the supplied **unmodified original mod archive** and the user's **currently installed/patched source**, plus at least one whitespace/comment/quote-equivalent variation. Do not gate on an exact version, SHA, line number, fixed indentation or G-CET-inserted predecessor marker. Treat independent source-proof/control-flow anchors as authoritative; reject substantive behavior drift rather than making unsafe guesses. New stages must upgrade partial passes without undoing pre-existing optimizations. Add original→live→idempotent→drift CI contracts for every source-proven rule.
2. A GC pause overlapping a callback does **not** establish that callback as the producer. Shared source/function names do not prove duplicate values or safe caching. Unattributed bytes remain unattributed until proven. Profiler instrumentation must be bounded, capture-gated, minimally invasive and maintain legacy measurements.
3. Native-only CI compiles the **DLL plus checksum**; main CI imports a verified pinned DLL and packages **both Profiler and Resolver**. CI tests are not in-game proof. See [build contract](BUILD-PIPELINE.md).
4. Ordinary development commits, repeated CI, documentation and experimental DLL rebuilds **do not bump product versions**. Advance both products together only upon the user's explicit request or an agreed major milestone. Identify individual builds by run ID, commit and actual native SHA.
5. Respect user's real source of truth, preserve installed mods and reversible install/restore; do not invent savings, rewrite GC policy or patch live files to obtain a result.
6. Keep `HANDOFF` small and timeless. File research and task reports in their subsystem folder and significant outcomes in [history](../history/ACTIVE-STATE.md). Documentation-only commits must not spam CI.

## At the start of work

1. Check the branch HEAD, `VERSION.txt`, `Directory.Build.props`, `MANIFEST.json`, native/main workflows and recent Actions/artifacts.
2. Read [product status](../product/PRODUCT-STATUS.md) and relevant topic records from the [index](../README.md). Use history for past decisions, not as a substitute for live files.
3. Inspect the current source/capture. Implement source-safe solutions and run relevant tests; follow executable CI through terminal results, verify both outputs and separately record gameplay tests.
4. Update technical subsystem notes/history when material. Avoid unnecessary handoff changes or CI for conversation-only observations.
