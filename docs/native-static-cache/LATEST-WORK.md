# LATEST WORK — native cache ONLY (fluid)

**Active task:** Validate native player position and passive NEAR/FAR transitions in the actual modded game, then complete the first safely source-matched author-wake replacement. **Still experimental; no real author polling retired.**

**User evidence:** On 2026-10-16, Cyberpunk 2077 v2.31 / RED4ext 1.30.0 reported `G-CET Static Locations (experimental) 0.1.0 has been loaded` and successful REDscript invocation in the player's RED4ext log. The user played normally on their extensive mod stack. **This verifies DLL loading and apparent smoke stability, NOT player-world-position resolution, wake delivery or performance improvement.** The initial smoke manifest was empty.

**Developer changes since CI #20:** `01a5cb9` added bounded native RED4ext health logs, exact player-getter failure codes, a passive diagnostic-anchor API and C++ tests. CI #21 exposed **real premature FAR after arming**. `a67a884` fixed it using evaluation epochs: until the **next evaluated native tick** any newly armed anchor is UNKNOWN, not FAR; Linux regression tests pass. `d44d5fc` added **explicit opt-in** marker `G-CET.smoke-probe`: when present, one passive 40 m native anchor is created at the first valid player position, NEAR/FAR events are logged only on transitions. No mod callbacks/source rewrites. **Verify the latest independent Windows+Linux CI before distributing the updated smoke binary; these changes are not yet game-tested.**

**Next developer action:** Check CI for the latest native-code SHA and fix any failure. Then package the **matching CI DLL**, an **empty 16-byte GCAS v1 manifest**, and the `G-CET.smoke-probe` marker as an isolated smoke-test Vortex ZIP. Verify DLL SHA and ZIP contents; do not reuse the older CI #20 binary for this probe.

**Next user action (after green CI / matching ZIP):** Run normal gameplay, move beyond 40 m and return, optionally fast travel and reload; provide `red4ext-*.log`. Look for `G-CET Static: player world position VALID` and native probe NEAR/FAR. Any `GetPlayer` or `GetWorldPosition` failure message directs the next ABI fix. Do not do a Profiler capture yet. Smoke overlay must remain independently removable.

**Later developer gates:** CET and REDscript real RTTI access, compile G-RED adapter, restore/fail-open verification, source-specific wake lifecycle and first actual old-callback retirement measured before/after. The generic Resolver wake rewrite is not yet implemented. No production profiler/Resolver CI changes.

**Done when:** actual in-game player-position and event stream are proven, one real admitted owner has an opt-in reversible source-matched native wake replacing recurring discovery, and no independent author behavior is lost. If only the diagnostic task completes, move its proof to history/details and replace this active task with the next direct step.
