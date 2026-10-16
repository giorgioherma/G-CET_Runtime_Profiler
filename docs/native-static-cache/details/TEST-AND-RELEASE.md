# G-LookupCache — tests and release gates

## Automation that currently exists

- Independent `.github/workflows/native-static-locations.yml` runs on experimental branch **only when `native-static-locations/**` or the workflow changes**; docs under `docs/native-static-cache/` do not need native CI.
- Windows Server 2022: pinned RED4ext SDK C++20 build of `GCETStaticLocations.dll`, native unit tests, isolated DLL + checksum artifact. This proves compilation/linking, not engine RTTI binding in Cyberpunk.
- Linux: C++ spatial index (including owner runtime addition/tombstones, invalid position, subscriptions, overruns, 10k-index stress), Python compiler/source tests and Lua 5.4 CET/R4 mock contract tests.
- Current reference milestone: CI [#20](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38911428092), commit `51ce694`, both jobs green. **Future chats must replace this as latest when newer code CI passes**. See rolling [LATEST-WORK](../LATEST-WORK.md).

## In-game test order — deliberate narrow scope

1. **Native-only smoke**: compile/obtain the DLL from the matching successful CI SHA; package with an empty valid GCAS manifest (`GCAS`, version=1, count=0, reserved=0), under `red4ext/plugins/G-CET-StaticLocations`. Do not add a CET/R4 author override. Backup/Vortex managed overlay. Expect **zero performance improvement**; purpose is safe load and ABI verification.
2. Startup + loading a save; confirm RED4ext loader and no crashes. Move and check coordinator status 2 and tick metric 11 increasing; invalid-player metric 12 should not dominate ordinary gameplay. Native declarations/API visibility from CET/REDscript still need independent verification.
3. Pause/menu/load/save/fast travel (and high-speed travel). Confirm session and UNKNOWN behavior, no stale FAR/NEAR, no missed resync when events occur.
4. Compile the separate REDscript declaration/optional G-RED adapter against compatible mods, and smoke-test its owner registration, journal read, fail-open and cleanup.
5. Only then: one **source-matched** opt-in owner with author-original fallback in a separately generated game-root overlay. Compare baseline/off/on and verify real retiring work, exact gameplay, rollover, reload, failure and mod disabling.
6. Automate a safe generic resolver transformer **after** examples demonstrate source proof and equivalent wakes; do not invent a mod-specific approval list for random installations.

## Release/packaging rules

- Native CI's DLL zip **is not** the final optimization pass; it may include development README. Resolver-generated ZIP must contain **only game-root deployable files**, e.g. `bin/`, `r6/`, `red4ext/`, one Vortex-managed overlay winning conflicts. No manifest of uninstalled historical mods; no PDB/dev docs in final game-root ZIP.
- Recommend Vortex for reversible single-zip overlay. For manual application, **back up entire CET mods + REDscript scripts folders before applying ANY optimization**, plus affected RED4ext files when included.
- A client-reported gameplay bug must permit per-mod opt-out and restoration of that mod without discarding unaffected optimizations. Source owner dependency graph governs regeneration if shared compiled data is involved.
- Never promise uniform performance gains across stacks; the 1000+-mod development stack received exceptional manual investigation. Future stacks prioritize broad generic rules and large measured offenders only.
- No version bump, production profiler/Resolver CI modification, or promotion of experimental binaries without explicit user decision and matching tests.

## Developer-only passive native probe and health logs (2026-10-16)

To prove the **existing native engine callback's real player coordinates**, use a **matching freshly built DLL** and an otherwise-empty valid `G-CET.locations` (GCAS v1, 16 bytes, no anchors). Add an **empty marker file named `G-CET.smoke-probe` beside the DLL** for this one experimental test. Without the marker no automatic anchor is created and the normal DLL remains passive.

On the first valid native player position the marker enables **one purely diagnostic XYZ radius-40m anchor** at the player's location, using the reserved high Int32 owner/ID range. Source mods are untouched, no CET or REDscript mod callback is registered, and there is **no gameplay activation**. The native log emits `diagnostic anchor ARMED`, then `diagnostic anchor NEAR` on initial evaluation, `FAR` after leaving 40m and `NEAR` upon return. Reloads/position loss invalidate the probe; it can be recreated at the new valid player position. No more than 32 diagnostic log messages are emitted over a plugin load, preventing dense-scene log floods.

Native `GCETStaticLastPlayerError` returns **-1** (inactive/not sampled), **0** (valid), **1** (GameInstance GetPlayer failed/unavailable), **2** (no player RTTI class), **3** (GetWorldPosition absent), **4** (native getter invocation failed), **5** (nonfinite/out-of-bounds world position). Native `GCETStaticProbeHere(radius)`, `GCETStaticProbeId()`, `GCETStaticProbeRemove()` allow explicit testing when the compatible RTTI exports are reachable, but the file marker needs no CET bridge and is safer as the first live probe. Never assume these functions are CET-callable until actually proven.

**Interpretation:** The user's original 2026-10-16 RED4ext log proves the native plugin **loaded** into v2.31 alongside existing mods; it does not prove world-position sampling. Native code compilation and pure C++ state tests do not prove actual game RTTI. The probe intentionally supplies no optimization benefit. Failure of this opt-in probe does not justify turning off author callbacks or putting sample anchors into production.

**Expected game-only observations:** correct load, bounded log, player VALID or a specific failure reason; diagnostic NEAR/FAR after actual movement; no unintended mod starts; stable save reload/teleport/menu behavior; removing the marker and Vortex overlay returns to no diagnostic source. This is an additional gate, not a substitute for per-mod source behavior comparison.
