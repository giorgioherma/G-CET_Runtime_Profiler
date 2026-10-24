# G-CET development agent instructions

**Repository:** `giorgioherma/G-CET_Runtime_Profiler`. **Development branch:** `dev/resolver-profiler-handoff` unless the user changes it.

Before changing generators, read `docs/HANDOFF/CORE.md`, `docs/resolver/PROJECT-CONTRACT.md`, `docs/LATEST-WORK.md` and the relevant per-mod/source documentation. Follow the active task and user-approved architecture.

## Normal development cycle

The user profiles a running Cyberpunk 2077 mod stack, then we analyze measurements and agree on optimizations (generic, native/C++ handoff, cache, or mod-specific semantic A/B/C extensions). **Do not require original source archives during investigation or design.**

Once implementation begins, use the active stack's **untouched CET originals**, maintained in the private `giorgioherma/original_cet_source` repository (or accessible ChatGPT Project Sources when testing locally). The user resets this read-only source baseline between stacks. The private repository also contains `capture/CET_Resolver_Input.json`, a **fixed unoptimized-stack capture for reproducible CI generation**; it is not the user's changing gameplay Profiler evidence. CI must not run new profiling or decide which optimization to implement. GitHub Actions checks out the private repository with the read-only `GCET_STACK_SOURCES_TOKEN` secret; public forks/untrusted PRs must not access it.

Treat an untouched `<mod>/init.lua` as the original-source **mod presence gate**, not merely its directory. A generated-only file (e.g. `GCET_Semantic/*.lua` or compiled/native integration output) has no required original counterpart, but its expected production, imports, ownership and dependencies must be validated. Omitted files irrelevant to the approved transformation are acceptable. **If a transformation requires an omitted original, fail/report only the exact missing file with its full game-relative CET path** (for example, `bin/x64/plugins/cyber_engine_tweaks/mods/ExampleMod/modules/logic.lua`), so the user can restore that individual file. Never request a whole mod directory merely because one file is absent; never lower an approved optimization's acceptance checks or silently skip it. A dependency whose exact path cannot be established is a blocker requiring investigation, not a guessed file path. Never substitute optimized files for originals.

Do not commit or publish third-party original mod files, entire source archives or temporary extracted originals. Keep only small independent regression assertions/fixtures and delete temporary working copies after validation. No REDscript baseline is required for ordinary CET Lua optimizations; request additional input types only when a concrete transformation needs them.

## Implementation invariants

- **Repair the existing authoritative generator.** If output damages author functionality or an earlier optimization, fix that rule at its source. Never add repair-only semantics, duplicate workers or a later patch that repairs a broken pass.
- **Start from untouched supported author input**, never a previously optimized ZIP as a generation prerequisite. Previously optimized files are useful only as preservation references or staged compatibility inputs.
- Compose in one Resolver pass: **generic 0-Engine → native integration → final source-aware semantics**. Final semantics must account for earlier staged generic/native edits.
- Preserve gameplay, original callbacks and function boundaries, external coordinator ownership, wake/dormancy, guard ordering, scheduler behavior, lifecycle, fallbacks and dependencies. Stay source-safe and version-fluid; no Lua SHA/version locks.
- Preserve established external `GCET_Semantic` workers; generate required hook and worker together in the existing authoritative mod-owned implementation, without duplicate execution.

## Final original-source acceptance (CI + agent, after implementation)

**CI must independently verify generation from private untouched sources and the fixed baseline capture; the agent additionally checks optimization behavior that automatic contracts cannot prove.** For every affected mod:

1. Using the active stack's **untouched CET files**, actually run or exercise the complete existing Resolver generation path (generic → native → semantic) in **one composed pass**. Do not substitute a previously optimized input, isolated rule call or synthetic fixture for this check.
2. Inspect the generated Lua against the input and the rule's intended behavior: source/function integrity, old and new optimizations, cheap guards before costly getters, external worker import/ownership, 0-Engine/native dependencies and safe fallbacks. Validate syntax when a Lua parser is available.
3. Verify idempotence and relevant partial/staged compatibility; add a **small permanent regression assertion** for any defect discovered. Fix failures only in the existing generator and rerun.
4. Run the existing GitHub CI to completion and confirm relevant jobs and artifacts. **Green CI alone is not proof that the real-source check passed.** Report separately: generator implementation, CI, real-source equivalence, and gameplay validation. Do not declare completion if any required check could not be performed.

The private-source CI workflow is `.github/workflows/original-cet-source.yml` (currently a **draft, not yet attached to the main build and not yet execution-verified**). Until connected and proven, CI green **does not** assert private-source validation. Never publish private source/capture files as workflow artifacts or commit them to this public repository. Historical Phase 1+2 optimization references apply to the current recovery work, **not** to future optimization development in general.

Keep significant work and discovered blockers updated in `docs/LATEST-WORK.md`.
