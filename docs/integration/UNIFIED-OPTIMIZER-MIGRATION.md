# Unified Cyberpunk 2077 optimizer — migration and product boundary

**Decision:** 2026-11-04; **game target:** Cyberpunk 2077 v2.31 as the current *tested game setup*, not a hardcoded universal script/source version lock.

**Product repository:** [`giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31`](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31), development branch `dev/resolver-profiler-handoff`.
The old [TOTAL Profiler](https://github.com/giorgioherma/G-Cyberpunk2077_Total_Profiler) is a historical UI / synchronization / correlation reference **only**. Its old CET DLL, legacy installers, GRSP alpha dependencies and duplicated lifecycle code must not be migrated.

## User-approved outcome

One straightforward **Windows desktop app** allows normal users to select the game directory once, verify dependencies, optionally install CET and REDscript profilers, run one synchronized in-game recording, collect/correlate CET + REDscript + optional CapFrameX measurements, analyze supported installed mod source, preview generated optimization passes, and manage verified restore/recovery. The normal path should feel like **Setup → Measure → Optimize → Results**, with advanced source/method diagnostics hidden unless requested. All risky mutations require explicit action and a reviewable report.

**Profiler consumers must work when only CET, only REDscript or both are installed.** CapFrameX is optional, including the ability to point to an existing copy; no exclusive version locks. Do not modify third-party CapFrameX binaries or user-owned settings except narrow, reversible settings when explicitly requested.

## Source of truth / code ownership

| Component | Authoritative repository / branch | Integration rule |
| --- | --- | --- |
| CET profiler / Lua instrumentation / transactional native installer | `G-CET_Runtime_Profiler` / `dev/resolver-profiler-handoff` | Reuse shipped standalone binary / documented headless API; do NOT recopy native source, add a second CET profiler or recreate install/restore |
| CET Resolver / generic 0-Engine → native → generic finalization → semantics / G-GC | **same CET repository / branch** | Existing authoritative engine remains in place; use `--capture --game [--generate-pass]`, preserving original/partial/idempotent source proof |
| REDscript profiler / RED4ext native measurements | `GRSP-G-REDscript-Profiler` / `dev/resolver-profiler-handoff` | Consume its existing CLI and complete developer-level CSVs + compact summary; add missing attribution or job evidence in its own Rust/manager implementation |
| REDscript framework | `G-REDruntime_Framework` / currently `main` | Independent shared REDscript runtime; integrate only with complete source/compile/lifecycle proof, never through a shadow copy |
| Generic REDscript Resolver | New unified product repository | **Separate .reds source-aware generator backend, same UI**; prioritize broad generic rules. No extensive default semantic library; optional user-specific semantics only by explicit decision |
| CapFrameX | Independent upstream tool | Link an installed binary and capture directory; import/copy results only, and preserve license/distribution and source integrity for any future bundling |
| Unified launcher, guidance, synchronized orchestration, combined analysis, component packaging | **New unified product repository** | Own no duplicate native profiler hooks / mod-specific author source; call backend headless interfaces; own only genuinely combined outputs |

The new product may package **verified exact standalone component artifacts** produced by their own CI, with a component manifest recording repository/ref/run/artifact/hash/license. Rebuilding the source projects separately in CI is permitted; never substitute an old TOTAL or unverified payload. A product release requires valid provenance checks, installed-file ownership checks and one-click recovery; v2.31 in the repo name is a game-target label, not an excuse to pin arbitrary mod source hashes.

## Verified existing headless integration points

**CET Profiler** `G-CET-Runtime-Profiler.exe` (same service as standalone GUI):

```text
--status  --game <root> --json
--install --game <root> [--core-only] --json
--collect --game <root> --json
--reset   --game <root> --json
--restore --game <root> --json
--emergency-restore --game <root> --json
--report --capture <folder> --json
```

**GRSP** `G-REDscript-Profiler.exe`:

```text
--status --game <root> --json
--install --game <root> --json
--title <title> --game <root> --json
--collect --game <root> --json
--restore --game <root> --json
--report --capture <folder> --json
```

**CET Resolver** `G-CET-Resolver.exe`:

```text
--capture <folder> --game <root> --json
--capture <folder> --game <root> --generate-pass [--pass-output <zip>] --json
```

The external GUI must not guess backend status from file presence alone, and must not auto-run install/restore or overwrite current live mod folders. Invoke commands with proper quoting/argument lists, parse structured exit and output, and report partial backend failures. The GRSP `--collect` contract requires the game be closed. CET F10/F11 are distinct user-owned capture bindings, not something the launcher may globally replace. Keep existing F10 COMBAT-POLICE-120 protocol and user-controlled F11 unrestricted capture. Unified synchronized capture cannot be claimed tested until game-side first/second key behavior across GRSP/CET/CapFrameX is verified.

## Measurement integrity and diagnosis

- Use CET's raw time/allocator/GC/scheduler/native data and GRSP's complete `RSP_FunctionMap`, `RSP_CallSites`, `RSP_SharedTargets`, `RSP_Cadence`, `RSP_WrapperChains`, `RSP_WorkMap` and runtime per-owner/per-function/per-frame sources; JSON top-ten lists are not exhaustive.
- Reuse producer `interop.contractVersion=1.0` where source-defined; correlate timestamped events and CapFrameX frame times by a verified synchronization method. Do **not** sum CET and GRSP `exclusiveMsPerSecond` as total game CPU, or subtract both from a rendered frame to invent a native remainder.
- An old capture never determines the currently installed mod inventory. CET scans the current live CET tree; REDscript scans the actual current `r6/scripts` tree and compile dependencies. Unknown/unmeasured does not mean idle.
- REDscript native call-edge instrumentation is not LuaJIT GC instrumentation. Any GRSP allocation attribution must be engineered and tested independently with reliable REDengine source/ownership boundaries; do not copy the G-GC collection policy.
- Report source/CI/compile and **gameplay-measured** results separately; keep environment config (RAM, mod stack, save/scenario) attached to comparisons.

## Safe source transformation and deployment

**CET** generator is **not moved into a duplicated new implementation**. The unified product triggers its existing authoritative code and consumes its generated ZIP and manifest unchanged. Preserve generic 0-Engine → native integration → generic finalization → source-aware semantics, prerequisite workers, fail-closed drift and second-pass recognition.

**REDscript** generator is developed independently using full source proof, current-owner inventory, safe generic rules first and optional explicitly chosen stack-specific semantics later. Source-first generic transformations are allowed when behavior can be proven without a capture row but must not invent measured savings. Generated files preserve original `r6/scripts` paths, signatures, mod dependencies and compile order. Validate the **whole composed stack with the supported redscript compiler** before offering an optimization. Never ship third-party original mods or silently edit them; output changed-file overlays, record every added/modified file and restore the original exact versions transactionally when deployment is explicitly requested.

## Development milestones (status as of creation)

1. **Foundation / guided UI:** new repo, accessible single-game-folder setup, optionally linked component paths, read-only detection and current backend status, clear advanced mode, explicit experimental/unsupported labels. Initially exact standalone products are not bundled.
2. **Verified binary packaging + capture:** CI downloads or builds **exact verified current** CET Profiler, CET Resolver, GRSP and optional CapFrameX under license, records immutable artifacts and SHA-256, runs existing lifecycle commands, preserves per-profiler restore and synchronized F10/F11 semantics with fail-close recovery.
3. **Combined results:** preserve native archives; add one normalized session manifest, verified time alignment, separate CET/REDscript metrics and frame hitches, and one simple user-facing verdict.
4. **Generic REDscript optimization:** use GRSP developer evidence and current `.reds` source, add missing profiler instrumentation in GRSP only, compile-gate a small reusable set of actual generic rules, and prove original/partial/optimized idempotence.
5. **Unified optimization/restore:** one workflow to analyze, compare, generate both kinds of passes, preview all changed files and verified dependency outcomes, then explicitly deploy and restore through the owning engines.

The work must **not** divert resources into a broad REDscript semantic library or replace G-CET's completed generation pipeline. Unfinished phases are not silently marketed as implemented.

## Handoff process

- CET and GRSP maintain their own release branches and dev branches; update in their authoritative generator/instrumentation first. Shared **unified** integration work happens on the new repository's `dev/resolver-profiler-handoff`.
- Build the unified app with `.NET 8` Windows tooling and GitHub Actions. Source and reproducible artifacts, not provenance of AI assistance, determine integration ownership. The old TOTAL Profiler may inform UI/correlation but is not a dependency.
- Keep [CET active work](../LATEST-WORK.md), [CET startup](../HANDOFF/CORE.md), [build pipeline](../HANDOFF/BUILD-PIPELINE.md) and GRSP [Resolver handoff](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/blob/dev/resolver-profiler-handoff/docs/RESOLVER_HANDOFF.md) aligned when contracts change.
