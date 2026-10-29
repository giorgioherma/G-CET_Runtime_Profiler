# Native-only owner-zero LuaJIT method stages — 2026-10-29

## Authority and boundaries

- Repository/branch: `giorgioherma/G-CET_Runtime_Profiler` / `dev/resolver-profiler-handoff`.
- Source baseline: native rebuild [#94](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/40634579638), successful, commit `7259194943ea843e0b0ffe5b0719f45fb1df9c7c`.
- Source implementation: [bce9ec5](https://github.com/giorgioherma/G-CET_Runtime_Profiler/commit/bce9ec519bee4679a759f329132023f20e10bae6). Native rebuild [#95](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/40794412835) started from this commit; its completion and game validation are distinct gates.
- Do **not** pin the new DLL in the managed Profiler/Resolver workflow, edit their reporting, bump v1.3.3, or deploy a release without explicit user approval. Native-only experimentation must not rewrite, suppress or cache actual Lua references.

## Evidence and measurement

PDB-symbolized CET 60.026-second capture `CET-20261028-215929_THE_FUCKING_TROUBLEMAKERS`: owner-zero LuaJIT positive growth **85.574 MiB / 673,723 ops**, not live memory or mod-owned retained heap. Disjoint native boundary totals: PURE 26.788 MiB, SCRIPT OVERRIDE 20.004 MiB, NATIVE PROXY 14.321 MiB and OUTSIDE 24.460 MiB. OnAction pre-dispatch alone: 49,861 calls, 149,583 positive allocation operations and 25,778,137 bytes (three ops / 517 bytes per invocation in this capture). NativeProxy: 82,061 calls, one 183-byte operation per call. Bounded native sample stacks place proxy userdata construction in `Scripting::ToLua`, weak references in `FunctionOverride`, and outside-scope closure construction at `WrapNextOverride`. Samples are not a population-based estimate.

## Source-only implementation

Existing authoritative `Patch-CET-v1.37.1.ps1` now samples around root/recursive `WrapNextOverride` call-site **construction only** (before protected Lua execution). Existing `EndPreDispatch` forwards its snapshot to a bounded per-method table in `CETRuntimeProfiler.h`. No extra CET observer, new Lua call, closure lifetime extension, `next()` interception, parent RTTI dereference, allocation-time heap allocation, GC policy change, or gameplay behavior change. The class identity and subscriber owner remain `NOT_PROVEN`; method hashes are not mod identities. Fixed capacity: 256 slots **per stage**, five stages, with dropped-stage-call counters and reset at both capture start and reset.

New raw native CSV: `CET_Runtime_Profile_Native_MethodStages.csv` (not yet integrated into managed reports). Stages: `PURE_ARGUMENTS`, `OVERRIDDEN_ARGUMENTS`, `NATIVE_PROXY_ARGUMENTS`, `OVERRIDE_ROOT_NEXT_CLOSURE`, `OVERRIDE_CHAIN_NEXT_CLOSURE`. Columns include method hash, calls, owner-zero positive bytes/ops, dropped calls and explicit unknown ownership. Argument-stage rows subdivide the pre-existing native pre-dispatch measurement; closure rows measure different bounded construction windows. **Do not add them to existing G-GC totals**, extrapolate PDB samples, or claim avoided allocations.

The new native-only Windows verification checks that the compiled DLL contains the CSV marker. Existing stable native DLL+manifest pin remains unchanged. Native-only source assertion runs before the complete historical CET Windows compilation. CI source assertions do not establish stack/ABI safety, game boot, capture compatibility or improved FPS.

## Acceptance and next measurement

1. Complete native-only #95: compile pinned CET source, PDB/dll pairing, actual DLL string verification, native SHA sidecars, developer-symbol artifact.
2. Install new DLL manually only for isolated test after preserving old ASI and its restore path. Validate cold boot, world load, no crash, combat capture and stop/export; no change in previous CSV totals beyond measured overhead.
3. Run established COMBAT-POLICE-60 same save/stack/equipment/CapFrameX timing; verify `Native_MethodStages.csv` contains real method hashes, `OnAction` pure-argument totals are consistent with pre-dispatch baseline, and no unexpected drops.
4. Inspect native proxy method hash `12899831133459796585` and root/recursive wrapper stages, which should distinguish conversion from closure creation, but not yet reveal the mod owner. Use source-only additional provenance if necessary, not pointer/RTTI guesses.
5. **Separate follow-up discussion**: whether CET can avoid repeated `WeakReference`/`StrongReference` materialization. Never share/copy invocation-captured `next` closures, suppress global OnAction, or cache native handles without proven alias, lifetime, reentrancy and subscriber guarantees.

Status (2026-10-29 00:42 UTC): **native #95 GREEN, gameplay/performance unverified**. Compiler, original CET rebuild, native verification, binary CSV marker and both artifacts succeeded.

## Exact native-only build artifacts (CI #95)

- DLL: [native payload](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/40794412835/artifacts/12863403445) — SHA256 `c2974d29444862ec23a6955c628ac6ca20148bc88cd5f0226241eb24ebe7ccaa`.
- Matching developer-only PDB: [native debug symbols](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/40794412835/artifacts/12863059414) — SHA256 `b57846386d6ad37b32613d0372fb06059e57cadada84f8a33fb1a573725d414a`.
- GitHub runner confirmed both output archives, historical linker validation, source asserts and DLL-resident `CET_Runtime_Profile_Native_MethodStages.csv` marker. This establishes compiled instrumentation, **not** a successful boot/capture, safe native dispatch optimization, or allocation savings.
- Do not change main `MANIFEST.json` pin / Profiler / Resolver packages or claim official release.

## Native #95 imported into main-package pin — 2026-10-29

User explicitly requested including [native CI #95](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/40794412835) in the managed Profiler/Resolver package. Its source commit `bce9ec519bee4679a759f329132023f20e10bae6` was **already an ancestor** of development branch; no cherry-pick or second native implementation. Native CI build verified DLL SHA256 `c2974d29444862ec23a6955c628ac6ca20148bc88cd5f0226241eb24ebe7ccaa` and separate developer PDB SHA256 `b57846386d6ad37b32613d0372fb06059e57cadada84f8a33fb1a573725d414a`. Main build now pins exact run ID 40794412835 plus manifest checksum and a compiled-DLL marker gate; raw `CET_Runtime_Profile_Native_MethodStages.csv` is archived and the PDB remains separate. Native identity `2.15.9-native-method-stages` is diagnostic only; product **v1.3.3 unchanged**. CI import/packaging must be checked separately; no startup/gameplay or allocation reduction claims. Preserve the former native #94 as the rollback baseline.

### Managed package acceptance (2026-10-29)

[Main CI #2155](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/40796437960), head `1ed51699b7270fb5f1ea37928bd819dd38bc533f`: **completed SUCCESS 6/6**, including the pinned native #95 artifact download, SHA-256 verification, DLL stage markers, managed build, untouched-CET source generation, and separate portable package upload. Artifacts: `G-CET-Runtime-Profiler-v1.3.3` (ID 12864785040) and `G-CET-Resolver-v1.3.3` (ID 12863469720). This establishes source/build/package import, **not** a cold game boot, capture, method-stage data quality or actual allocation reduction. Keep native #94 and PDB rollback/testing evidence separately.
