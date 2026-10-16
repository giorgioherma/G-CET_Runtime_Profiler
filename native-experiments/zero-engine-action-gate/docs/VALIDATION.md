# Validation record — isolated native prototype (2026-10-16)

## Performed

- Linux CMake/Ninja C++20 **Release** build of the action-gate shared library and independent C++ tests completed successfully.
- CTest: `gcet_native_gate_tests` **1/1 passed**; the suite checks ABI and null handling, disarmed/incomplete fail-open, exact hits/misses, duplicate actions, wildcard, reset/rebuild, 4 concurrent readers while rebuilding, and counters.
- Tests use `CHECK`, **not Release-disabled C++ `assert`**. Earlier Release-only test harness was strengthened before documentation was finalized.
- GitHub experiment was created on `exp/native-zero-engine-hotpath-20261016`, starting from `dev/resolver-profiler-handoff` commit `c18e220e8efda558c6d47257facde617da0b59e1`, without merging back.
- Confirmed experiment-versus-base file diff includes only `native-experiments/zero-engine-action-gate/**`; there are **no main CI, native CI, profiler, Resolver, production 0-Engine or pinned ASI/DLL changes**.
- C++20 heterogeneous `unordered_set::find(std::string_view)` avoids the temporary string allocation that C++17 lookup would otherwise produce in the hot decision loop. This is a code-level property; runtime microbenchmarks and Windows DLL tests are not yet available.

## Not performed; do not infer

- **No Windows/MSVC binary built or hash pinned.** Linux `.so` cannot be installed as a CET/Windows DLL.
- **No experimental Lua copy has been executed against a LuaJIT/CET session.** Its `ffi.load`, `ActionRouter.AttachNativeValues`, `GetInterestSnapshot` and lifecycle transitions still require isolated integration tests.
- **No early CET native per-subscriber hook exists in this branch.** Decision values cannot prevent native-to-Lua marshalling without that independently verified host integration.
- **No game installation, profiler/Resolver CI, active game capture, GC/CPU saving or FPS improvement is claimed.**
- The previously measured collector stalls are not addressed by a C++ action-name index on its own. It is not a collector or scheduler replacement.

## Acceptance gate for a later early native stage

Only propose a CET-side host patch after proving: exact per-subscriber call site after distinguishing 0-Engine's observer from other mods, valid native action name without unsafe RTTI dereference, lossless fallback for no/unknown name, complete subscription mirror invalidation, preserved ordering and all dynamically changed subscribers, no global suppression of `OnAction`, and reliable game-load/capture-off/on/restoration with separately pinned DLL/PDB. If any requirement cannot be met, leave the native decision core observational.

## 2026-10-16 — native microbenchmark and isolated Windows validation setup

- Independently reconstructed the exact C++ ABI v1 source and existing CTest fixture, compiled with GCC 14.2 C++20 Release/Ninja on Linux and ran `ctest`: **1/1 passed**. This is a local rerun, not a Windows/CET runtime test.
- Added `tests/bench_action_gate.cpp` with 2,000,000 sequential decisions per case. Local Linux Release measured **59.7 ns** per disarmed fail-open lookup, **68.3 ns** per exact-action hit, **67.1 ns** per exact-action miss and **68.2 ns** per wildcard fail-open. These are environment-specific **native C ABI lookup costs only**, excluding native CName decoding, CET dispatch/argument marshalling, FFI and Lua. They are not frame-time savings.
- Added `.github/workflows/native-action-gate-experiment.yml` restricted to this isolated experiment branch and path, which builds MSVC x64 Release, runs existing CTest, prints the standalone benchmark, and uploads **standalone** DLL/EXE artifacts. This does **not** modify the existing profiler/Resolver/native production workflows. Windows CI results must be observed separately after push; at authorship time this was not yet verified.
- See `docs/NATIVE-CET-SHADOW-TEST-PLAN.md` for the required **shadow-only pre-Lua test** before any native skip; that integration is not implemented here and no game effect is claimed.
