# Native G-GC method-context and time-stratified sampling experiment

**Implementation source committed; native rebuild and game validation pending.** Product versions remain **v1.2.7**. Do not import a new DLL into public Profiler/Resolver until the native-only CI succeeds, the exact DLL SHA is pinned, and main CI succeeds.

## Why

Fourth capture `CET-20261014-170941_FOURTH_G-GC_CAPTURE.zip` conclusively separated new allocations (`oldSize==0`) from true realloc growth and identified native converter pathways `ClassConverter::ToLua`, `WeakReference`, `StrongReference`, and `Enum` with a matching CET PDB. However the bounded **2,048** native samples were exhausted at **305.4/519.8s**, losing late-police-chase stacks. The existing CET boundary types do not cover game-originated calls into `FunctionOverride::HookRunPureScriptFunction`, `FunctionOverride::HandleOverridenFunction` or `NativeProxy::Callback`. The opportunity is to identify the game RTTI function, **not** to bypass Lua/Sol object creation or auto-rewrite mods.

## Actual native changes

- `Patch-LuaJIT-GC-Source.ps1`: fixed **4 native unwind slots per each of 1,200 capture seconds, plus 4 overflow slots**. The original global sequence-based candidate rate (every 1,024 owner-zero operations, with a large-event exception) is unchanged. Only the first four qualifying events in a given second are unwound, making late windows sampleable even in long captures. Skipped candidate count remains separate from exact population counters. Read-back compacts sparse per-second slots chronologically into CSV; no phantom zero entries. Static allocation is about 4,804 samples, bounded with no heap allocation in a LuaJIT allocator callback.
- Sampling payload adds a **64-bit native RTTI function hash** from capture-only TLS. A native `ggc_trace_method_set` setter saves/restores previous value; `MethodScope` also sets/restores the nested CET bridge context. No string formatting, RTTI enumeration, Lua callback, lookup or reference caching on the allocator hot path.
- `Patch-CET-v1.37.1.ps1`: three narrowly placed game-to-Lua **argument marshalling** boundaries: `CET_FUNCTION_OVERRIDE_PURE`, `CET_FUNCTION_OVERRIDE_SCRIPT`, `CET_NATIVE_PROXY_CALLBACK`. Each is scoped around ToLua reference/argument conversion; the existing `ExecuteChain`, override `next()` and native game call continue outside that extra scope. No changes to `FunctionOverride::Context` layout, observer registration, callback semantics or original CET getters.
- `CETRuntimeProfiler.h`: extend bridged native counter/timeline arrays from **6 to 9** categories, preserving the original six labels/indices, and append **`NativeMethodHash`** to `CET_Runtime_Profile_GGC_UnscopedNativeSamples.csv`; existing CSV columns and legacy counters retained. This hash is a native RTTI function identifier in the **sample only**; a hash/call boundary is not a mod owner, argument type diagnosis, or a proof of avoidable allocations.

## Invariants and next actions

- Do not alter GC pacing/thresholds or suppress native wrappers globally: original converters may enforce reference lifetimes, values and aliasing.
- Capture OFF remains dormant; no allocation-time C++ strings or Lua calls. Owner-zero exact byte/operation totals unchanged by sample selection.
- Sample group counts are not additive to whole-population bytes, and per-second dropped qualifying candidates are **selection loss**, not missing allocation counters. Long-duration overflow >1,200 seconds is explicitly bounded.
- Compile native on pinned CET 1.37.1 and its historical MSVC; verify matching PDB/DLL. If the patch fails to apply, or failed smoke tests imply source/SEH layout changed, leave current known-good native hash and main package unchanged.
- After build: pin exact successful native CI run and DLL SHA to the combined Profiler+Resolver; add compiled feature checks to main CI and verify both packages. Gameplay test: capture-OFF world load, observation/override, native proxy events, HUD/menu/sleep, collector report, 8–10m sample coverage across all phases, no new crashes. Run source-linked analysis only on same-build PDB.

## PASS3 identification — dominant RTTI method resolved

Capture `CET-20261014-211612_PASS3_FIXED_QUEST_ACTIVE_AND_NATIVE_ADDITIONS.zip` samples identify the dominant nonzero `NativeMethodHash` as **`9658066991423262154`**, appearing in **744/1,248 bounded native samples**. This value is an **exact FNV-1a 64-bit match for the string `OnAction`** (FNV offset `14695981039346656037`, prime `1099511628211`). CET's FunctionOverride source passes `apFunction->fullName.hash` into the capture scope; CET/RED4ext source also uses `FNV1a64(acFullName.c_str())` in RTTI lookup. A separate hash-to-string dump is therefore **not needed to learn the function short name for this dominant sample**.

**Important scope:** `OnAction` is a method name shared by multiple classes. The hash alone does **not** identify `PlayerPuppet`, a Lua owner, an observer, or the precise allocation source. The source stack contains many OnAction registrations including PlayerPuppet; correlate native boundary, class/function identity and capture callback multiplicity before inferring savings. The 205.8 MiB pure-override population is a boundary category, not proof that this one method/owner produced all its bytes. A future optional capture-only class identity record could disambiguate classes without globally modifying wrapper behavior.


## Remaining OnAction attribution — after exact-input routing

The dominant PASS3 method hash `9658066991423262154` resolves to the unqualified method name `OnAction`, but method-name hashing does not distinguish `PlayerPuppet::OnAction` from similarly named methods on other classes. Before claiming native-pure-override memory savings from Lua observer consolidation, gather **bounded class-qualified attribution**, including the owning RTTI class, native boundary, observed call count, and per-boundary allocation-growth values. Do not infer a class from the name hash alone, or sum overlapping boundary populations. Make this an instrumentation-only, bounded-per-distinct-method diagnostic; do not allocate method strings on each hot callback. Compare pre/post native proxy/override and Lua observer call counts on like-for-like captures.

`immersive_third_person` now has a dedicated exact-action routing semantic rule with the original Observe fallback. Its action interest is finite and proved by source, unlike DriveAerialVehicle's configurable action lists and queued full action data or CallList's localized substring matching. These latter consumers are **not** safe candidates for finite exact-name routing without further source evidence. Never replace their dynamic behavior with guessed lists merely to decrease registration counts.

