# G-LookupCache — CORE

**Naming rule:** Use **G-LookupCache** in new documentation. Keep existing `GCETStaticLocations.dll`, `GCETStatic*` RTTI functions, `G-CET.locations`, source paths and legacy runtime display names unchanged until an explicitly tested migration.

## What we are building

A **single shared native spatial service** for Cyberpunk 2077's source-proven fixed locations, consumed by CET and REDscript. Replace redundant periodic author-location discovery with efficient native distance/state transitions **where equivalent wake semantics are established**. The objective is **retired recurring work**, not merely an additional cache that runs alongside the old scanner. No gameplay feature is activated solely because the player enters a native radius.

## Repo, branch, boundaries

- GitHub: `giorgioherma/G-CET_Runtime_Profiler`; **active isolated branch:** `dev/native-static-anchors-experimental`. Verify live HEAD on every continuation; do not reuse stale SHA descriptions.
- Native source root: `native-static-locations/`. Experimental CI: `.github/workflows/native-static-locations.yml` (Windows RED4ext SDK DLL + Linux C++/Python/Lua tests).
- **Leave production profiler native CI #84 and production Resolver/Profiler code and pipelines alone** unless the user explicitly redirects. Build independently; the native DLL is a prospective **Resolver-generated game-root payload**, not an automatic profiler component.
- G-RED framework is a **separate repository** `giorgioherma/G-REDruntime_Framework`. Prefer optional source overlay and existing scheduler; do not modify its stable core without separate authorization.
- Main product target is eventual **one application, two pages, three primary actions, one game-root input**. Cache integration must fit this without another player config screen. This combined UI is not yet implemented.

**Generic-pass identity:** Location optimization is a reusable **source-structural Resolver pass**, not a per-mod semantic rule list. The goal is to suspend source-proven location-dependent recurring functions or safely separable branches across arbitrary installed quest/gameplay mods; preserve all independent phone, PC/terminal, message, quest/fact and lifecycle listeners. A whole callback may sleep only if *all* its remaining obligations can safely sleep or wake equivalently. Reviewed named-source pilots are fixtures, not generic admissions. See [Resolver evidence and arming](../details/RESOLVER-EVIDENCE-AND-ARMING.md).

**Separate location and remote wake responsibilities:** A phone listener or quest event does not invalidate a proven fixed-location optimization. Preserve the independent event listener; retire only proved recurring location discovery. FAR_DORMANT owners awaiting proximity must keep their native anchor armed. Fully inactive location stages must disarm the native owner so idle native tracking can sleep. See [Resolver evidence and arming](../details/RESOLVER-EVIDENCE-AND-ARMING.md).

## Non-negotiable behavior

- A **single RED4ext Running-state coordinator** owns player-position sampling; CET/REDscript consumers must never become competing update writers.
- States are `NEAR=1`, `FAR=0`, **`UNKNOWN=-1`**. Missing DLL, invalid/unavailable player, reset, disarm, tombstone, subscriber resync or source drift means **original author behavior/fail-open**, never inferred FAR.
- One shared **8192-event fixed history**, independent consumer cursors; initial owner/global snapshot and same-thread `AckSnapshot`; lost events require resync. Runtime events must never be silently stolen/lost.
- Owner/anchor IDs must be unique positive Int32. Anchor added from source is initially **disarmed**; author stage explicitly arms. Changed source/editor geometry gets a **new ID**; removed ID is tombstoned and cannot be reused during the loaded manifest.
- Keep quests, remote starts, UI/hotkeys, entity cleanup, sessions, save/load, fast travel, active gameplay and exact author interaction radius. Retire only fully proven original wake paths. Distinguish source-authored wake from unrelated work sharing its callback.
- Work from the **effective installed game mods** selected by the existing game-root input. Historical `modsLIVEnow` / R4 archives are **regression fixtures**, not proof of current installation or final opt-in.
- Native profiler metrics describe **service work**, not saved Lua/REDscript work or FPS. Measure before/after and old-subscriber removal separately.

## Current truth versus intended goal

The native index, event journal, coordinator, runtime owner registration, CET bridge, optional G-RED scheduler bridge and source-matched R4 fallback bridge are **implemented experimentally** with Linux/Windows CI. They are **not** a production pass. Neither automatic generic Resolver source rewriting nor full gameplay-equivalent author callback retirement on the user's stack has been proved. REDscript overlay has not been game-compiled. Player world-position RTTI and initial native NEAR were observed in-game; FAR/return and full consumer integration still need gameplay verification. Native CI #30 validates zero-armed-owner dormancy. The generic CET+REDscript evidence scanner remains read-only triage; a **strict, single-location, source-local CET job-pair family** now generates reversible native-wake overlays in `lookup-cache-resolver/tools/generic_cancelable_lane.py`. This narrow structural automatic admission does not prove arbitrary named quests, helper calls or mixed-purpose functions safe and has not been game validated.

For the exact **current next action and CI**, read [../LATEST-WORK.md](../LATEST-WORK.md). Do not copy dated statuses into CORE.
