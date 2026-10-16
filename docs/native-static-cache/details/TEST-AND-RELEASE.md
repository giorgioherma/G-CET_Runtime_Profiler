# Tests, CI, smoke and release gates

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
