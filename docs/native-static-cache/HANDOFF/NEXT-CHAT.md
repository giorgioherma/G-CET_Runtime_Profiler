# G-LookupCache — NEXT CHAT

**Current developer objective:** generic CET+REDscript source-evidence -> per-callback Resolver admission -> source-safe native lifecycle rewrite. Read `lookup-cache-resolver/README.md`, `docs/native-static-cache/details/RESOLVER-EVIDENCE-AND-ARMING.md`, and `docs/native-static-cache/LATEST-WORK.md`. The native player getter already worked in the game; do not restart native smoke testing by default. Phone/quest listeners are independent, not automatic candidate vetoes. Differentiate FAR_DORMANT needing an armed native wake from fully inactive disarmed stages.

> Continue work on **G-LookupCache**, the shared native static-location cache, GitHub repository `giorgioherma/G-CET_Runtime_Profiler`, branch `dev/native-static-anchors-experimental` (do **not** switch to `dev/resolver-profiler-handoff`).
>
> First read **`docs/native-static-cache/HANDOFF/CORE.md`**, then **`docs/native-static-cache/LATEST-WORK.md`**, **`docs/native-static-cache/HANDOFF/WORKFLOW.md`** and **`docs/native-static-cache/HANDOFF/DOCUMENTATION-RULES.md`**. Read the relevant `docs/native-static-cache/details/` files and live source files only as needed. Query current GitHub HEAD, branch tree and **native-static-locations.yml** Actions history before editing. Ignore the giant global `docs/LATEST-WORK.md` unless another task explicitly requires it.
>
> Native cache goal: one RED4ext main-thread player coordinator, one source-owner static spatial registry, independent CET and REDscript subscribers, fail-open UNKNOWN/resync/lifecycle, and **actual retirement of source-proven redundant proximity polling**. Keep independent quest/story/UI/save/load work alive; do not claim a DLL or mock bridge alone saves gameplay CPU. Existing experimental CET, G-RED and source-matched R4 bridges are under `native-static-locations/adapters/`. Game-side integration and general safe Resolver transformation remain unproven until verified.
>
> Continue the **one current task** in `docs/native-static-cache/LATEST-WORK.md`; do not expand scope into unrelated profiler/resolver pipelines or the separately maintained G-RED runtime. Do not touch native Profiler CI #84 or production Resolver unless instructed.
>
> **Document as you work:** keep HANDOFF stable; modify subject-specific `details/` files for lasting contracts; put completed/failed milestone evidence in `history/`; update the same **short, replaceable** `LATEST-WORK.md` with only current direct steps and blockers. At completion archive outcomes, then **replace LATEST-WORK with the next agreed task**, not a chronological accumulation. Always report specific commit, CI verification and remaining real-game risks, and say explicitly whether I need to test something.

This prompt remains version-fluid; do not hardcode a release number, DLL hash or current CI run into the reusable handoff.
