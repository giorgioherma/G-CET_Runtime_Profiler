# CET 1.38.1 / Cyberpunk 2077 2.5 — public profiler port

This is an **isolated test branch**. It does not replace the 2.31 public release on main. No redesign or new public profiling metrics.

## Authoritative inputs

- CET 1.38.0 release notes: game 2.5 (3.0.88.46235), dropped earlier game versions, fixed hook-registration deadlock, RED4ext SDK/engine ABI migration; xmake >=3.1.
- CET 1.38.1 release notes: further RED4ext SDK and RTTIHelper updates.
- Upstream pinned source: maximegmd/CyberEngineTweaks commit 213151fa22c79182922a08a44e4a44d44739d1cd.
- Xmake 3.1.1, latest available Windows MSVC toolset on CI, SDK 10.0.26100.0.

## Exact native hook differences

- CET 1.37.1 registered hooks directly under FunctionOverride::Override() with a blocking mutex.
- CET 1.38.1 uses try_lock, queues PendingOverride, calls ApplyOverride immediately or via ApplyPendingOverrides before mod onUpdate. Do not remove this upstream deadlock prevention.
- The profiler binds its native Context* metadata **inside ApplyOverride**, after Context creation, so deferred and immediate hooks are treated identically.
- The exact requested **script** class name is carried through the pending queue; using CClass::name would relabel e.g. PlayerPuppet::OnAction to an engine-native alias.
- CET's Context member structure is unchanged. Only new internal PendingOverride fields and ApplyOverride's private signature are extended. No registration-time Lua environment dereference.
- Observe, ObserveAfter, Override, and per-mod ScriptContext event timing remain on the original authoritative Lua call sites.
- Override next()/wrappedMethod() timing boundaries now exit **before** CET error handlers call luaL_error; no profiler Scope(nullptr) surrounds a Lua longjmp site.
- RTTI, CStackFrame and argument-container layout changes come from upstream CET 1.38.1 and its matching RED4ext.SDK. The profiler does not reuse old 2.31 native offsets.

## Packaging

New workflow: .github/workflows/cet-2.5-profiler-package.yml.

Native code changes are built separately by `.github/workflows/native-profiler-rebuild.yml`. The portable workflow reuses the exact hash-locked native-only artifact (currently native build #42437606920, source commit `8b7f743c`, SHA256 `f06d00a684f57c93bcf90c9f8fbeb1c4ba56a9ada25904d98cee6a55621b370f`). The packaging build proves the native generating source is unchanged from that successful build commit and refuses source drift rather than silently importing stale binaries. The patched CET native hook structure was verified in successful full-package run #42412422208; new native revisions must repeat this verification and update the pin before packaging.

The portable workflow applies the CET 1.38.1 target and exact profiler hash to the staged manifest, builds the .NET 8 app and launcher, and runs the existing install/collect/restore and regression smoke tests. It uploads **the staged directory directly** to GitHub Actions. GitHub Actions creates the download ZIP itself, so only **one extraction** is needed; no nested release ZIP or redundant `Compress-Archive` exists. The artifact includes the optional validation mod beside the app. The isolated test build never modifies main, release tags or Nexus.

This iteration accepts the exact official CET ASI supplied in CP2077-2.5_port.zip (sha256 f0acdad154bd0033583e0f6e675ea18b4bdb60cda28133d792a4674e6d7f228a). It is **not proof** that every independently packaged CET distribution is byte-identical.

Optional independent Lua counter fixture: validation/CET-1.38.1/GCET_25_Validation/init.lua, packaged beside the application and installed manually only for the test.

## Validation state

- Original 2.5 CET source and existing public instrumented call sites inspected.
- Hook-source contract implemented in tests/CET25PortContract.ps1.
- Native compile, test-ZIP build, packaging layout and in-game world load must be reported separately.
- In-game verification remains mandatory: normal/control world load, instrumented world load, callback counts, exact class names, delayed registration, Override pass-through, CSV output, successful manager collect/restore, no duplicated registrations.
- Do not claim game stability, fps gains or exact callback count equivalence without live capture.

See the CI run/artifact itself for the build result; job success alone is not proof of gameplay.

## 2026-11-08 backport: composed adaptive dispatcher and developed capture controls

The v2 adaptive bridge registered a second onUpdate inside the 0-Engine sandbox. CET 1.38.1 replaces the previous handler; the v3 installer instead captures and dispatches the original callback plus its temporary Scheduler in the same CET registration, with original last-registration-wins semantics. The installed source is restored transactionally. Preexisting unsafe v2 bridges are refused; restore the original installation before applying this build.

ProfilerControls adopts the developed in-game timer HUD and existing native-clock COMBAT-POLICE-120 F10 auto-stop, preserving independent unlimited F11 and optional scenario tags. It also carries passive half-second heap sampling with bounded native markers and established 0-Engine workload snapshots (only when all four related services exist). CI scripts verify installation and Lua structure; gameplay timer draw and native callback attribution remain separate live validation gates.

The optional GCET_25_Validation mod now exports independent callback-count checkpoints through bounded `GCET25_V1_*` native markers rather than unsupported sandbox `io.open`. The archived `CET_Runtime_Profile_Markers.csv` contains the series when the profiler is capturing. The first marker after F11 is the baseline; subsequent deltas give independent measurements for Observe, delayed Observe, ObserveAfter, Override, onUpdate and onDraw. The installer never installs the validation mod by default.

**2026-11-08 native build evidence:** source commit `8b7f743c` compiled successfully in [native CI #42437606920](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/42437606920), artifact `gcet-native-profiler-cet-1.38.1-cp-2.5` ID `13600383223`. The rebuilt DLL SHA256 is `f06d00a684f57c93bcf90c9f8fbeb1c4ba56a9ada25904d98cee6a55621b370f`; it includes callback function environment ownership with a native cached-counter fast path. Packaging uses the pinned binary, never old tracked payload. This proves the native source compiles and was built, not that attribution is correct in gameplay.
