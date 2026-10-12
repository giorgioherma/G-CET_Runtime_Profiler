# Active project state — 2026-10-12

**Snapshot, not canonical code:** confirm branch HEAD, run results, most recent capture, and source before acting. This page intentionally separates source/CI proof from user in-game verification.

## Repository, package and release safety

- Repository: [giorgioherma/G-CET_Runtime_Profiler](https://github.com/giorgioherma/G-CET_Runtime_Profiler).
- Development branch: `dev/resolver-profiler-handoff`.
- **Managed Profiler and Resolver package version: 1.2.3**, shared via `VERSION.txt`, `MANIFEST.json` and `Directory.Build.props`; launcher version resource aligned. Building on a dev branch does **not** mean immutable public v1.2.3 release has been published.
- **Native payload remains 2.15.0**, SHA-256 `c62d57f50b1fd792ae989cb78fb54cee8d82b0370abdb51ea01617c2ddcfa23e`. CI guard explicitly covers package 1.2.3. Do not accidentally package unvalidated native 2.16.
- Last source/semantic baseline immediately before the 1.2.3/documentation updates: **CI #969 GREEN**, HEAD `ae73f48d636bcab940255db4791add4a77414ecd`. Check the **latest** [Actions build](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/workflows/build.yml) before calling current HEAD green.
- Canonical Windows build: `.github/workflows/build.yml`; standalone Resolver artifact plus portable Profiler package. Version numbers identify product, CI run numbers identify iterations. Don't bump version for each rule by default.

## What is established

- One source-proven Resolver: current live mod files; generic first, semantic second, marker/postcondition recognition; compose already partial 0-Engine integration; generated ZIP only, no silent changes to user's CET.
- User reports consistently large measured reductions, often **more than 50% CET workload** on large tested stacks after generic and semantic optimizations. Functionality was not exhaustively tested across every feature.
- Generic frame consolidation, finite input routing, enabled `PLAYER` shared-provider fallback; remaining shared-provider candidates largely analysis-only.
- 0-Engine Scheduler + `GCETWorkQueue`, `GCETPhasePlanner`, `GCETFrameListeners`, `GCETStateSignals`. Services opt-in, no automatic modifications to every mod.
- Native/managed burst and GC evidence integrated into review. Current GC evidence **does not independently authorize AUTO rewrites**.

## Recent mod-specific work and live validation gaps

### Street Games Playable Poker — unusually aggressive location optimization

- Existing `streetgamespoker` semantic generates **two files** from author source: `init.lua` four-lane PASS4 Scheduler + original fallback, and `StreetTables.lua` PASS4C 25m squared-distance near/far world wake.
- Target cadence: realtime active gameplay every frame; seat 0.10s; near world 0.20s; far world 1.0s. Preserve map pin category/registration, table spawn/despawn/trace, menu/render, author startup/shutdown.
- Version tolerance is behavioral/source-shaped (including differing delta-time parameter spelling), not exact mod/ZIP versions. Two-file generation, test fixture, version variant and idempotent rerun passed in CI #969.
- User already corrected their installed broken copy. **Acceptance still requires original author archive → current Resolver → generated two-file output**, then poker interaction and map-pin checks in game. A synthetic source-shaped test is not proof of gameplay on every version.

### Tunnel Rescue / Big Adam

- Existing `tunnel-rescue-v2` semantic generating injector was fixed to expand named callback capture groups correctly, preventing literal placeholder text being shipped as Lua.
- Preserve author's outside 150ms decision cadence, tunnel-active per-frame Life/Swimming/Street work, state transitions and cleanup, targeted quest mappins and diagnostic cache.
- User already applied a local source fix. Permanent acceptance is original mod through newly built Resolver. No separate broken-output repair rule.

### Starman / Ziggy

- `ziggy-last-play-navigation-sentinel-v3` combines the previous v2 navigation sentinel and new offered/far quest gate in **one semantic** to avoid rule conflicts.
- One 193.5s capture: `onUpdate` about **15.49 ms/s**, `onDraw` about **2.18 ms/s**, largest `onUpdate` spike **228.86 ms**; roughly **49.49 MiB positive allocation**, **423,407 allocation operations**. Quest observed in unaccepted `offered` state with 0 collected pieces. Metrics specific to that capture.
- Rule intent: cheap frame-level distance/quest-state probe; if unaccepted/offered, stable session, no dialogue and >130m from Milo, run full controller every 0.25s. Near giver, dialogue, active story/accepted state, uncertainty or failure: original per-frame cadence. Keep author's elapsed-time accounting, journal/shard discovery, dialogue, pin correction and lifecycle.
- Source-shaped CI passed with latest semantic tests, but **full quest and matched before/after reduction must still be tested in-game**.

## G-GC evidence: top open research

- Experimental ~443-second capture reported ~**1,457.8 MiB positive allocated growth**, **12.18M allocation operations**, **2.21M object births**, **559.65 MiB (38.39%) unattributed**. Positive allocation is not retained heap; unknown and native/C source do not give proven Lua caller.
- Raw global finalizer counter anomalies remain **unvalidated**; do not use as optimization evidence until independent test.
- Known source-proven allocation example: AutoLoot temporary world-pin candidate table elimination, with unchanged author's native query/cadence. Matched actual gameplay benefit pending.
- New targets: high-frequency unnecessary full queries, repeated Lua table rebuilds, redundant large lists, cross-mod read-only shared-state opportunity **with full alias/freshness/invalidation proof**, meaningful dormant states with cheap wake, and **rare very large data producers** which may cause isolated hitches.
- Native 2.16 experimental source callsite addition previously showed a repeatable before-F11 world-load crash on same save/stack; isolated successful 443s capture does not clear release gate. Native 2.15 pinned until crash and overhead characterized.
- Distinguish collector time from triggering callback, sampled C-callsite vs executing/source owner, sustained byte pressure vs episodic spikes. Do not auto-tune collector.

## Immediate execution order

1. Verify the current 1.2.3 GitHub Actions CI run and consistency of the Profiler and Resolver version info, manifest and native SHA; no version bump implies new native instrumentation.
2. Research the **newest representative allocation-heavy capture and corresponding mod source**. Rank by positive bytes, allocation ops, object births, sampled source functions and rare spikes separately. Identify confirmed redundant work vs necessary data production and unknown provenance.
3. Seek reusable low-risk **generic** patterns first. Only admit a GC-motivated transform with source proof and broad payback, preserving existing generic+semantic composition.
4. Use existing semantic state/cadence framework for big offenders: quest dormancy, cheap proximity probes and rapid full wake. Preserve story/input/UI behavior.
5. When fresh generated mods are tested, validate Poker's pins and interactions, Tunnel's load/quest steps, Ziggy's first shard and full quest progression. Compare matched captures and report any feature regressions.

Maintain this page after any substantial pass, measurement schema change, native promotion, or gameplay validation. Do not present a prior CI number as the current build without checking it.
