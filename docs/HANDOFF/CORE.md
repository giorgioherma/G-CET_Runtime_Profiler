# G-CET — core context for EVERY new development chat

**MANDATORY FIRST READ.** This is the compact current-state entrypoint, not a historical log. Read this file **first**, then [BUILD-PIPELINE.md](BUILD-PIPELINE.md) for CI/packaging, [PRODUCT-STATUS.md](PRODUCT-STATUS.md) for functional progress, and [ACTIVE-STATE.md](ACTIVE-STATE.md) for detailed chronological work. Verify GitHub HEAD, manifest and latest Actions before executing changes. **Updated 2026-10-14.**

## Repo, branch and verified build

- Repository: `giorgioherma/G-CET_Runtime_Profiler`.
- Working branch: `dev/resolver-profiler-handoff`; **do not assume the current HEAD SHA from this document**. Query the live branch before every task.
- Latest **verified combined** build at this snapshot: **v1.2.7**, [Build G CET Runtime Profiler run #38476578740](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38476578740), **all four jobs green**, with `G-CET-Runtime-Profiler-v1.2.7` and `G-CET-Resolver-v1.2.7` artifacts. Read current `VERSION.txt`, `Directory.Build.props` and `MANIFEST.json` for updated versions.
- Current included native DLL: `profilerNativeVersion=2.15.1-scalar`, SHA-256 **`89d848b775fb67bc5e56043053f07f315eb414d46940eb7d343f9eaea3ab095b`**. Compiled by the **native-only** [run #38473605694](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38473605694) and imported by the **main** build. The `native-profiler-build/v2.11.0/` directory name is historical and does **not** mean the DLL was intentionally downgraded to functionality from 2.11.
- Latest CI success proves compilation/packaging and source contracts; **it does not establish in-game stability or GC savings**. **UPDATE (2026-10-14):** a valid new-native 185.908s capture was inspected, with complete GC/deep/timeline evidence and no dropped rows. **Gameplay/menu use remained normal for some time AFTER recording**, then an unusual exit from Wannabe Edgerunner Cyberpsychosis preceded missing Esc/inventory menu access and bed sleep-time UI stall. This is *not* an immediate stop-path symptom. Suspect in-game status/state transition or merged-pass stale code as **hypotheses**, not proof. NPC Outfit Manager `onUpdate` 3.037→0.189 ms/s and G-GC allocation ops/s 803→30 vs an older uncontrolled capture strongly support idle-gate effectiveness; see [capture analysis](NEW-NATIVE-GC-CONTINUATION-2026-10-14.md). Reproduce psychosis UI issue with capture off first; test 2.15/native-current A/B only if needed. The prior known-good native 2.15 is archived in previous successful releases/builds.
- **Do not mistake archived `ACTIVE-STATE.md` entries describing native 2.15 or product 1.2.5/1.2.6 as the current release.** The current `MANIFEST.json` plus successful matching Actions run wins.

## Product overview and status — 60 seconds

**Profiler:** standalone portable Windows .NET 8 GUI + CLI/launcher, replacing CET's ASI transactionally, preserving original files and keybindings, integrating conservatively with 0-Engine Scheduler, capturing callback CPU, call rates, spikes/bursts, G-GC allocations/objects/phases, adaptive deep callsites and optional CapFrameX frametime. `Collect` archives raw data, generates HTML + `CET_Summary.json` + `CET_Resolver_Input.json`, and safely restores user files. **Operational and extensively regression-tested**; novel native scalar probe is available but requires explicit instrumented callsites before measurements appear.

**Resolver:** separate portable app over shared core. Input = real collected capture + **current user-installed source read-only**. It detects existing/partial optimizations, runs safe generic AUTO first and source-proven semantic transforms second (composable), and creates **one ZIP of changed user-present files**, never silently overwriting live mods. Current production includes a source-pattern, fail-closed **NPC Outfit Manager idle radar gate and mutation-state revision**. Many other source-specific rules already exist; inspect `resolver-knowledge/semantic-library.json`.

**Current research:** cut unnecessary LuaJIT data creation/GC at the producer; distinguish measured work from estimated opportunity. Deep source-attribution reporting has been integrated. Cross-mod **Better Loot Markers + AutoLoot** inventory-display sharing remains *research, not a shipped rule*. NPC Outfit Manager's current cache is an **in-memory revision/reference mirror**, **not** a persistent full last-edited look. No proven before/after GC reduction or full gameplay signoff yet.

For fuller component and progress table: [PRODUCT-STATUS.md](PRODUCT-STATUS.md).

## The TWO build workflows — NON-NEGOTIABLE CONTRACT

| Workflow | Responsibility | What it publishes |
| --- | --- | --- |
| `.github/workflows/native-profiler-rebuild.yml` — **Rebuild G-CET native profiler** | Compile and verify native `cyber_engine_tweaks.PROFILER.dll` **only** | Native DLL + SHA-256 artifact; **never Profiler/Resolver portable apps** |
| `.github/workflows/build.yml` — **Build G CET Runtime Profiler** | Retrieve a **pinned successful native CI artifact**, verify SHA-256/required symbols, compile/test/package managed **Profiler and Resolver** | **Both** `G-CET-Runtime-Profiler-vX.Y.Z` and `G-CET-Resolver-vX.Y.Z` |

**Native change packaging chain:** modify native source → native CI green → record its run ID and actual DLL SHA → update main build's pinned native run and manifest native SHA/revision → **main Build G CET Runtime Profiler** green → verify *both* package artifacts and embedded native hash. **Do not bump the product version automatically**; change the shared product version only under the version policy below. Never package apps in native CI; never claim that successful native-only CI updated the normal Profiler package; never leave an unverified build running while reporting completion. See [BUILD-PIPELINE.md](BUILD-PIPELINE.md) for detailed commands/contracts.

## Product version policy — user-controlled milestones

- **Do NOT increment `VERSION.txt`, `Directory.Build.props`, or `MANIFEST.json.packageVersion` for each commit, workflow run, rebuilt DLL, bug fix, documentation change, or repeat CI run.** Any number of builds can share the same current version.
- Advance the **Profiler and Resolver product version together** only **when the user explicitly asks for a version bump** or **for a substantial milestone that the user has agreed should be versioned**. Do not arbitrarily declare every technical task a milestone.
- Continue making commits, testing and uploading Actions artifacts under the **existing version** between releases. Use Git commit SHA / GitHub Actions run ID / native DLL SHA-256 to identify the exact build, not a forced version increment.
- When a version bump is authorized, update all three product-version fields **atomically**, then package Profiler + Resolver through the **main** workflow and verify both artifacts. A native DLL's hash/pin or accurate native revision metadata may change for a native rebuild **without** advancing the product version.
- Keep draft/development builds and milestone releases distinct; do not republish or overwrite an official release/tag as a side effect of routine CI.

## Invariants you must preserve

1. **One Profiler + one Resolver.** No third optimizer; internal decision-layer name stays **internal** and never appears in player-facing app/report/installer/release text.
2. Real capture, source and currently installed mods are authoritative. Generic = low-risk broad mechanisms; semantic = measured, significant source-proven system improvements (standard **3 ms/s exclusive** admission floor, explicit exception only with robust rationale). Never let G-GC alone authorize a rewrite.
3. Preserve all author gameplay/features: quests, shards, map pins, UI, input, timing, cleanup, errors/fallback and mod/session lifecycle. Resolve original→generated output; no ad hoc repair patches as product solution.
4. **Version-fluid generation:** match structural and behavioral source patterns, not file hashes or specific mod versions. Fail closed on changed/ambiguous author logic; verify compatible-source variants, composition, already-applied state, idempotence.
5. Native instrumentation is capture-bounded, low-overhead and non-invasive. No extra arbitrary getter calls, global handle interception, stale native userdata or unconditional Lua table dumping. Preserve existing native metric schemas and new optional output compatibility.
6. Do not edit the user's live CET mods merely to generate a pass. Backup/restore correctness and hashes are part of the product.
7. CI ≠ gameplay proof. Never invent observed savings or native correctness.

## Start-of-chat execution checklist

1. Open `docs/HANDOFF/CORE.md`, `BUILD-PIPELINE.md`, `PRODUCT-STATUS.md` and latest beginning **and end** of `ACTIVE-STATE.md`.
2. Query branch HEAD, `MANIFEST.json`, `VERSION.txt`, both workflow definitions, latest **main and native** CI runs/artifacts. Current code/CI > old handoff snapshots.
3. Open subject-specific evidence/spec/docs *only as needed*; load current source/capture, then implement and test.
4. For nontrivial changes update the **short current snapshot** here, detailed history in `ACTIVE-STATE.md`, and deep topic records in the appropriate topic document. Keep references/runs/hashes concrete.
5. Follow the relevant CI job **to completion** in the same active working session where possible; fix errors, rerun, verify artifacts and report gameplay limits.

## Deeper sources by task

- Engineering invariants and rule architecture: [PROJECT-CONTRACT.md](PROJECT-CONTRACT.md).
- Locked data-provenance design: [DATA-PROVENANCE-SPEC.md](DATA-PROVENANCE-SPEC.md).
- GC reasoning and high-priority research: [GC-RESEARCH.md](GC-RESEARCH.md).
- Source-verified deep reuse / NPC idle / cross-mod loot ideas: [AUTOMATIC-DEEP-REUSE-REPORT-2026-10-13.md](AUTOMATIC-DEEP-REUSE-REPORT-2026-10-13.md).
- Full chronological work and previous CI decisions: [ACTIVE-STATE.md](ACTIVE-STATE.md).
- Native + managed build wiring: [BUILD-PIPELINE.md](BUILD-PIPELINE.md) and [../../BUILDING.md](../../BUILDING.md).
- Code: `src/G.CETProfiler.Core/`, `src/G.CETProfiler.App/`, `src/G.CETResolver.App/`, `native-profiler-build/v2.11.0/`, `resolver-knowledge/`, `resolver-runtime/`, `tests/`.
