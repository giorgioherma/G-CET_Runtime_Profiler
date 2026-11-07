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

It builds a CET 1.38.1 CONTROL and instrumented profiler from the same source, validates the patched native hook sites, computes the actual profiler DLL SHA256, and applies the branch-specific CET 1.38.1 manifest only to the staged test package. Then it reuses the existing .NET 8 app/launcher build and package/transaction/restore smoke tests. It generates a **test ZIP**, never updates main, release tags, or Nexus.

This iteration accepts the exact official CET ASI supplied in CP2077-2.5_port.zip (sha256 f0acdad154bd0033583e0f6e675ea18b4bdb60cda28133d792a4674e6d7f228a). It is **not proof** that every independently packaged CET distribution is byte-identical.

Optional independent Lua counter fixture: validation/CET-1.38.1/GCET_25_Validation/init.lua, packaged beside the application and installed manually only for the test.

## Validation state

- Original 2.5 CET source and existing public instrumented call sites inspected.
- Hook-source contract implemented in tests/CET25PortContract.ps1.
- Native compile, test-ZIP build, packaging layout and in-game world load must be reported separately.
- In-game verification remains mandatory: normal/control world load, instrumented world load, callback counts, exact class names, delayed registration, Override pass-through, CSV output, successful manager collect/restore, no duplicated registrations.
- Do not claim game stability, fps gains or exact callback count equivalence without live capture.

See the CI run/artifact itself for the build result; job success alone is not proof of gameplay.
