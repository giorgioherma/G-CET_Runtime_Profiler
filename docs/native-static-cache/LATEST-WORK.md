# G-LookupCache — LATEST WORK (fluid)

**Current task:** Complete the first source-pinned, gameplay-equivalent CET native wake adoption for StreetGamesPoker, then extract its reusable proof pattern into generic Resolver admission. Branch `dev/native-static-anchors-experimental`. Do not touch production Profiler/Resolver CI, native Profiler CI #84, or the separate G-RED repository.

**Reviewed source:** The user-supplied `EXTENDED DORMANCY(7).zip` includes the precise source epoch already pinned in `reviewed_streetgames_pilot.py`: `StreetGamesPoker/init.lua` SHA-256 `e4594ed8eb7a1ad0a889068db0a5be258c498fab719a6f2ab13df408e6e9af84`, `StreetTables.lua` SHA-256 `a79edb7eca3632531d6a35d304857592a9f61f6f7c00337ecf9e0ff8346d0099`. All 40 source-roster sites and nine reviewed insertion points (including shutdown) were located. A local reproduction of the reviewed insertions parsed successfully with Lua 5.4 and reversed exactly to original text; that is **not** an in-game load or a production-generated gameplay validation.

**Implemented:** Shared native service, CET host, per-owner lifecycle, multi-anchor aggregate, original scheduler-job fallback, source-fingerprint-guarded reversible overlay, source-owned late adoption. New hardening in this continuation:
- Native consumer distinguishes successful original-job restoration from failed fallback, retrying unsuccessful outages instead of latching them.
- Source gate retains a retryable `fallbackPending` state when a scheduler operation or author's restore callback fails.
- Source-pinned poker overlay now detaches native ownership on CET shutdown, in addition to rollback and session stage changes.
- Shared host must schedule successfully **before allocating native source IDs**, avoiding irreversible tombstone loss on temporary host startup failures.
- Added Lua regression tests for scheduler restoration, consumer fallback retries and host-not-ready source admission; Python regression for the poker shutdown injection.

**CI:** Earlier source [#38998482461](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38998482461) and native [#38997945499](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38997945499) passed. New source [#39055713415](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/39055713415) passed; new native tests were triggered on the latest source/runtime commits; verify the **matching current HEAD run** before calling the new native patch green.

**NEXT:** Verify new native Linux Lua and Windows jobs at matching SHA. On any failure, inspect actual CI logs and repair. Then validate exact generated full overlay/native RTTI accessibility and CET load in the user's installed game: original vs optimized, FAR→NEAR→FAR, already-near startup, save/load, teleport, session close, shutdown and native loss, chair spawn/despawn, stranded safety, active poker and other independent listeners. Measure the authored FAR callback actually retired; do not claim FPS or Lua savings from mock tests. The user does not need to provide another archive or generic profiler capture before a source-safe test package is ready.

**Hard bounds:** First pilot is source-pinned and opt-in, not generic automatic admission; runtime source integration is not yet game verified. G-RED compilation and unrelated AreaLoot stay separate. Source archive is an external validation fixture, not installed-stack proof.
