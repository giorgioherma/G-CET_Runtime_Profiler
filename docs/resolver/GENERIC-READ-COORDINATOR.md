# Generic shared-read Lua coordinator — near-limit source adapter

**Working status (2026-10-22):** implemented; managed source contract
exercised. Full live-overlay and gameplay tests pending. The shared
getter algorithm is generic; an individually generated file-owner Lua
worker is **not** by itself a new mod-specific semantic optimization.

## Purpose and pipeline

The source-first generic 0-Engine provider pass previously introduced
two top-level locals per newly generated `Game.Get…()` helper. Mods
at LuaJIT's 200-active-local ceiling could not adopt these safe getters.
Before `NativeReadReuseSource.TryStage`, the Resolver now detects
its own canonical provider headers in high-pressure current files,
relocates only those generated headers into an owner-local
`GCET_Semantic/gcet_native_reads_<path-id>.lua`, and substitutes one
module import in the original file. The generated worker retains the
exact proven 0-Engine API resolution and original Game getter fallback.

This extraction is conditional on current source and worker body proof:
no Lua source SHA/version or fixed file count. Staged worker state is
examined before live state, compatible repeat generations are no-ops,
new current-source getters add new helpers to the same existing worker,
and foreign/custom worker edits fail closed. Never remove author
locals, reorder callbacks, retire event listeners, change cadence,
or replace a user module to create more capacity.

## Current source pressure

| Current installed file | Existing G-CET helper headers | Baseline active locals | Expected outcome |
|---|---:|---:|---|
| CourierJobs/init.lua | 2 | 200 | Extract helper pairs; adopt eligible getters |
| Dedrapanamgoondate/init.lua | 8 | 200 | Extract helper pairs; adopt eligible getters |
| hotscenes/hotscenes_nc_delights.lua | 9 | 199 | Extract helper pairs; adopt eligible getters |
| InfiniteGigs/init.lua | 1 | 200 | Extract helper pair; adopt eligible getters |
| hotscenes/init.lua | 0 | 200 | Deferred; no free top-level local for import |

The 20 canonical existing helper headers above match the supplied
`modsLIVE.zip` source. **These are prospective output effects.**
A new Resolver pass manifest and emitted Lua are required to verify
actual local counts, files, and callsites. Gameplay tests are still
needed to quantify tradeoffs of using a module table lookup.

## Contract test and follow-on

`tests/NativeReadReuseContract/Program.cs` proves synthetic 200→199
local capacity recovery, unchanged Game fallback, current-source
expansion, idempotence and installed-file immutability. The full
pass must additionally prove a clean 0-Engine method resolution,
unchanged author semantics, and no duplicate worker paths.

The remaining second-stage work is moving independently source-proven
0-Engine scheduling and wake coordination out of semantic mods while
preserving their existing native workers. This helper migration alone
does **not** claim that broader scheduler separation is complete.
