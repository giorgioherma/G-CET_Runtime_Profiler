# LATEST WORK — native cache ONLY (fluid active task)

**Current task:** Validate the native shared-location service in the *actual game* and establish the first source-matched, reversible author-wake replacement. **Not yet complete.**

**Verified baseline, 2026-10-16:** Branch `dev/native-static-anchors-experimental` HEAD `51ce694`. Independent [CI run #20](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38911428092) **SUCCESS** on that SHA: Windows RED4ext plugin DLL built and staged; Linux C++ spatial/coordinator/runtime-anchor tests, manifest compiler tests, and CET + R4 Lua mock bridge tests passed. The optional G-RED REDscript overlay is **not REDscript-compiled or game-tested**.

**Implemented:** native Running-state position coordinator, fail-open on invalid player position, compiled GCAS and runtime `AddAnchor`, per-owner arm/removal/tombstones, 8192-event independent subscriptions with snapshot/overrun recovery, 14 diagnostic counters, opt-in CET consumer, optional G-RED scheduler bridge, source-matched R4 Lua fallback compatibility bridge. Current repo code is the authority, not previous artifact ZIPs.

**NEXT — user-dependent native smoke test:**
1. Generate a **fresh smoke ZIP from CI #20's matching DLL**, with **empty valid GCAS manifest** and no mod source overrides. Confirm SHA against CI artifact.
2. User tests game startup, RED4ext load/no crash, load into a save, movement, save/reload/fast-travel and native RTTI health (`Ready`, coordinator state, tick/unavailable counters). Gather logs/error text if the DLL fails. **An empty manifest gives no performance gain.**
3. Verify `GameInstance.GetPlayer / GetWorldPosition` RTTI retrieval in the actual Cyberpunk build. If coordinator stays UNKNOWN, fix ABI before author-wake replacement.

**NEXT — developer-dependent after smoke:** Compile the optional REDscript overlay against the actual REDscript environment and verify the native functions are callable from CET and REDscript. Build the first **source-fingerprint-guarded** R4/admitted-owner integrated package using the *currently installed source* (not archived `modsLIVEnow` as a source of truth); validate original behavior versus native: initial-near, enter/leave, travel, load, remote quest wakes, fallback, user opt-out. Only then mark actual retired timer/callback savings. Generic Resolver source recognition/transform remains a later separate unimplemented stage.

**No action:** Do not run ordinary Profiler/Resolver CI or overwrite live mod folders. The next meaningful player test is **native smoke**, not another five-minute profiler capture.

**Done when:** Native game ABI and both runtime consumers are actually validated; at least one admitted real owner has a source-proven/rollback-capable native wake replacing the previous proximity discovery without breaking independent behavior; measured old-work retirement recorded. Until then: **experimental**.
