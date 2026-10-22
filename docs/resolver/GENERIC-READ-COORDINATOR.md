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


## 06:07 real pass and follow-up

The user's 06:07 pass applied **InfiniteGigs only**. It deferred
CourierJobs, Dedrapanamgoondate, Hotscenes NC Delights and Hotscenes main
for Lua locals. Four standalone provider relocation attempts (including
DedraJNCJoytoy) refused their existing header as `source drift`; the
Resolver now compares the **whole actual provider body** line-by-line after
normalizing cosmetic layout rather than requiring identical CRLF and
indentation. Differences in executable lines still fail closed; no
unknown author helper is deleted or overwritten.

The user explicitly approved **permanent English-only Hotscenes** for
their personal installation. Opting in using the Resolver checkbox or CLI flag
allows the locale subsystem and unused author locals to be removed first;
then `hotscenes/init.lua` is eligible for a separate generic read worker
despite not previously having provider headers. No localization is cut
for non-opted-in users. This is separate from the general 0-Engine
scheduler coordinator work and is not automatically enabled elsewhere.

Do not mark remaining Lua-budget cases resolved until a **new pass ZIP**
contains all source/worker pairs with budget under 201 and no source
conflicts. No source version or Lua SHA is used as an admission key.


## 07:21 pass: full four-owner source correction (2026-10-22)

The 07:21 #1842 live-stack result (one InfiniteGigs worker, ten completed
semantic stages, four Lua-budget deferrals, four generic-read coordinator
refusals) was **not** a semantic dormancy regression. The helper definitions
remained in their original Lua files; no worker was referenced at the
rejected stage. All **28** existing, G-CET-generated provider bodies match
the reviewed 0-Engine getter recipes:

| Source file | G-CET provider bodies |
|---|---:|
| `CourierJobs/init.lua` | 2 |
| `DedraJNCJoytoy/init.lua` | 9 |
| `Dedrapanamgoondate/init.lua` | 8 |
| `hotscenes/hotscenes_nc_delights.lua` | 9 |

`GenericReadCoordinatorSource.TryFindProviderBlock` now uses a literal
G-CET owner marker and source-bounded, unindented helper terminator rather
than the brittle multiline regex. It verifies every normalized executable
line against the current generated provider recipe. Unknown blocks are
never removed; if source semantics really differ, the generated failure
reports the first differing line, or absent/duplicate owner markers.

`tests/NativeReadReuseContract/Program.cs` also replays the **four
complete owner inventories**, at their representative near-limit local
counts and original CRLF/LF conventions: four worker stages, 28 moved
provider families, preserved author declarations and `Game` fallbacks.
A separate negative contract changes a fallback body and verifies refusal.

**Validation:** GitHub CI
[#1850](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/39743387947)
green on all five jobs. No new generated pass against the user's whole
current CET folder was available after this commit, and no in-game
behavior claim is made. The English-only personal cut is selected in
Resolver's own UI/CLI, not through a file in the CET `mods` root.

## 08:14 run: one missing C# interpolation, two affected phases

The four generic read-worker failures were **not** missing semantic worker
files, a stale profiler capture, or actual author Lua drift. The 08:14
manifest shows source block line 7 with real
`__gcetEngine.GCET.GetMappinSystem` versus the expected literal
`__gcetEngine.GCET.{recipe.EngineGetter}`. The generator's C# string
was missing `$` immediately before its opening quote. The fix is
**central**: all 26 shared-provider catalog families receive their
actual resolved getter in the generated expected header.

### Installed-stack audit

From `modsLIVER.zip`, the affected source inventory is:

| Measure | Source-audited total |
|---|---:|
| Lua files scanned | 2,051 |
| Existing G-CET provider helper blocks | 1,084 in 373 Lua files |
| Previously emitted unresolved recipe expressions | **45** |
| Affected Lua files | **16** |
| Affected owners | **2**: `marmurbank` (15 files), `DedraThirdPersonCombatAddon` (1 file) |
| Legacy source blocks matching canonical after correcting placeholder | **45/45** |

The unresolved expression is Lua-invalid; existing optimization files
with that bug cannot be left silently untouched. The new
`SharedProviderTemplateRepairSource` stage runs before generic worker
extraction and scans current installed/staged Lua source, but rewrites
**only** the single misrendered getter reference inside a source-proven
G-CET helper. It verifies the **whole helper**, including Game fallback,
against the current reviewed canonical recipe before staging. Unknown,
partially edited or foreign blocks are refused atomically and reported as
generation failures, not silently accepted or overwritten. It does not
remove author functionality, depend on a historic pass ZIP, or add Lua SHA
validation.

The generated manifest reports
`sourceOwnedMalformedProviderFilesRepaired`; repair transforms use
`GENERIC_PROVIDER_TEMPLATE_REPAIR` with the per-file repair count and
provider families. A repeat pass skips already repaired code.

Regression tests independently verify all 26 C# recipes (to prevent the
generator and its test sharing a broken template), generate/repair the
16-file/45-expression inventory, preserve live files, reject real source
drift, and verify idempotence. **CI and source comparisons are not
in-game evidence**; after a green build a new current-stack pass must
verify all four original read-worker failures are cleared and the 16
repaired files are emitted.
