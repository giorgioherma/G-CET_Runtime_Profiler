# Fourth G-GC capture — native PDB and allocation-producer narrowing (2026-10-14)

**Source:** `CET-20261014-170941_FOURTH_G-GC_CAPTURE.zip` (provided by user), complete 519.833252-second START/PAUSE. Production versions remain Profiler and Resolver **v1.2.7**, native instrumentation `2.15.3-unscoped-realloc-context`. Analysis only: no mod/Resolver/GC code changed.

## Scenario anchors (user-confirmed)

- 0–60s: idle.
- 60–130s: running only.
- 130–270s: combat.
- 270–519.833s: police pursuit, additional combat and running until heat cleared (mixed, **not** a homogeneous combat measurement).
- No explicit scenario markers within the archive; use user-provided times as authoritative approximate phase boundaries. CAPTURE_START at 0; PAUSE at 519833.2517ms. The archive cannot verify whether Esc, inventory and sleep UI worked after Pause.

## Native/capture integrity

- 43 ZIP members, ZIP CRC check passed; 43 entries include the new `Data/Metadata/CET_Runtime_Profile_GGC_UnscopedReallocOrigins.csv` alongside all legacy raw streams.
- Reconciliations in **both** `CET_Summary.json` and `CET_Resolver_Input.json`: `VmReconciled=true`, `ExtendedReconciled=true`, `ReallocOriginsReconciled=true`, `HasReallocOrigins=true`.
- Owner-zero exact positive growth: **564,857,080 bytes** (**538.689690 MiB**), **3,599,916 operations**. Positive growth is *not retained heap or proof of garbage*. Ratio to all G-GC positive bytes ~**35.922%**. VM_C **564,273,052 bytes**, ~99.897% of owner-zero positive bytes.
- All **564,857,080** owner-zero bytes fall `OUTSIDE_CET_INSTRUMENTED_BOUNDARY` even with the added CET draw/lifecycle/overlay scopes. This means no owner-zero allocation was measured within those particular wrapped entrypoints. It **does not** exclude CET or mods: game-to-CET FunctionOverride/NativeProxy callbacks do not use the existing native boundary scopes. No mod ownership may be inferred.
- Exact legacy path decomposition: `RESIZE_GROWTH` **560,898,464 bytes / 3,538,199 ops**, `NEW_GCO` **3,958,616 bytes / 61,717 ops**. The latter is a specific LuaJIT allocation hook, not an exhaustive census of every new GC object.
- **Decisive new result** *within RESIZE_GROWTH*: `OLD_SIZE_ZERO` **560,897,840 bytes / 3,538,168 ops**; `OLD_SIZE_NONZERO` **624 bytes / 31 ops**. The 64–255 B `OLD_SIZE_ZERO` tier alone accounts for **560,552,464 bytes / 3,531,964 operations** (mean positive delta ~158.71 B). Thus the high-volume path comprises new allocation requests, **not growing pre-existing buffers**. Both `POSITIVE_DELTA`, `ABSOLUTE_NEW_SIZE` and per-second origin partitions exactly reconcile to `RESIZE_GROWTH`, never add on top of it. Actual old-size-nonzero events are rare, mostly tiny.
- One-second native sample budget: **2,048 / 2,048 bounded native stack samples**, **1,467 dropped**; last sampled event ~**305.406s**. This truncates stack evidence over much of the final pursuit. Whole-population counters/timelines **were not dropped**. Never scale sampled bytes into whole-population shares.

## Verified exact native symbolication — genuine breakthrough

Retrieved the developer-only PDB from successful native [#38582048697](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38582048697) artifact `gcet-native-experimental-debug-symbols` and the **same-run** DLL from `gcet-native-experimental-scalar-observations`. Verified extracted DLL SHA-256 **`5c3d5dfad9e9131c1aae53f700985dfdd7b9d417067044687fbf32f8b78b0e11`**, matching manifest and PDB artifact DLL sidecar; PDB SHA-256 **`30ccf88c4f2d9515f05729b9473bc613f1d36392d54c29f2f8b4b48f9a658450`**. Used LLVM symbolizer with PE **preferred image base 0x180000000 + module RVA** (plain RVAs produced unresolved symbols). Exact same-built binary matched its PDB and returned source/function names.

| Native CET RVA (sampled Frame3) | Sample count | Resolved native symbol / provenance |
| --- | ---: | --- |
| `+0x892ea` | 779 | `ClassConverter::ToLua`, `src/reverse/Converter.h:196` (`make_object(..., ClassReference(...))`); continued via `Converter::ToLua`. |
| `+0x11c37e` | 538 | `sol::stack::push<WeakReference>`; next frame commonly `FunctionOverride::HookRunPureScriptFunction` or `HandleOverridenFunction`. |
| `+0xfd086` | 342 | `sol::make_reference<..., StrongReference>`; most often through `Scripting::ToLua` → `NativeProxy::Callback`, sometimes `FunctionOverride`. |
| `+0x93fa3` | 294 | `sol::detail::usertype_allocate<Enum>`; next frames `sol::make_object<Enum>` → `EnumConverter` / `Converter::ToLua`. |

These groups total **1,953 / 2,048 = 95.36%** of collected *sample events*, **not** 95.36% of all allocation bytes/ops. They converge on **CET's C++ game-to-Lua reference/value marshalling layer**. Other sampled paths include `FunctionOverride::WrapNextOverride` closure construction. First two frames in most samples were `lj_trace_hot` and `lua_newuserdata`; the native unwind is only 6 frames and often ends inside the converter (cannot generally name the initiating mod).

The four major previous-capture third-frame RVAs `0x8927a, 0x11c30e, 0xfd016, 0x93f33` differ from new respective offsets by +0x70 each, suggesting build-layout continuity, **not** valid cross-build symbolication. **Do not** use this new PDB to claim exact function identities in older DLLs.

### Upstream code checked

Official pinned CET commit `61bd6214f0f5f8748589c9e476538614a13908c0`:
- `src/reverse/Converter.cpp:44–64` iterates RTTI converters and creates a new `sol::object`; `Converter.h:191–197` constructs a new `ClassReference`.
- `src/scripting/FunctionOverride.cpp` handles game-originated Observe/Override dispatch. It constructs Lua `WeakReference` for the context and marshals game arguments. Some callbacks are invoked many times even when Lua body returns immediately.
- `src/reverse/NativeProxy.cpp:146–176` marshals each native proxy argument through `Scripting::ToLua` before Lua callback invocation.
- These wrapper objects can carry game lifetime, handle and aliasing semantics. Global wrapper caching or API-value substitutions are **not authorized** by the capture; changes could alter gameplay/GC/refcounts.

## User-anchored gameplay phases

CapFrameX synchronization reported **GOOD**. Combined CFX frame stream **26,777 frames / 519.367s**, mean FPS 51.557, p95 26.827ms, p99 33.514ms, 19 frames >100ms. Native first/last user windows are approximate and not controlled isolated A/B.

| User segment | Duration | Owner-zero MiB/s | Requests/s | Requests/rendered frame | CFX FPS | Collector milliseconds | Completed GC cycles |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Idle | 60s | 0.685 | 4,396 | 74.4 | 59.11 | 0 | 0 |
| Running only | 70s | 0.979 | 6,484 | 110.3 | 58.79 | 3,299.2 | 1 |
| Combat | 140s | 1.193 | 8,032 | 159.8 | 50.27 | 3,878.3 | 3 |
| Mixed pursuit/combat/running | ~249.833s | 1.048 | 7,031 | 145.5 | 48.43 | 7,041.7 | 4 |

Activity raises allocation rates **even per rendered frame**, but correlation cannot prove an individual allocation or mod is avoidable. The preceding third native capture had ~246.569 MiB over 238.249s (~1.035 MiB/s), extremely close to this capture's ~1.036 MiB/s overall; not an A/B measure, but the aggregate stream is reproducible across different durations/scenarios.

## Internal collector and actual hitch context

`CET_Runtime_Profile_GC_Collector.csv` reports **973,767 incremental calls**, **8 completed automatic cycles**, **14,186.915ms nested internal GC execution**, **no explicit or full collection**. Eight distinct `atomic→sweepstring` phase operations lasted **122.36–211.30ms**, at approx capture seconds **83.29, 128.62, 192.42, 217.97, 299.05, 332.49, 413.17, 445.31**. CapFrameX recorded long frames near each time; this supports an immediate long-frame collector contribution, not ownership of the original garbage source. Several other >100ms frames have no corresponding >100ms GC operation and require other explanations.

Scope-internal collector time overlaps callback CPU and cannot be added to frame workload totals. `sweepstring` ~4,294.679ms and `finalize` ~3,730.742ms are important portions of total, not distinct independent costs.

## Source-verified hook targets from the *live* `mods(5).zip`

The paired source archive contains **141 mod directories and 1,583 Lua files**; unchanged as input.

- `PlayerPuppet::OnAction`: **640,974 invocations / 519.833s ≈ 1,233/s** in each of six active observers: `0-Engine`, `immersive_third_person`, `nativeSettings_side_menu_add_on`, `CallList-VehicleDetailsOverlay-CET`, `hotscenes`, `DriveAerialVehicle`. For `0-Engine`, `init.lua:1332–1339` calls `ActionRouter.Dispatch` and legacy `Events.PlayerAction:trigger`. `modules/ActionRouter.lua:189–209` **already** uses PASS8/9 action-name indexed early-out; simply repeating Lua action filtering won't remove CET's **earlier** argument marshalling. The registration recorded **65,463,089 scoped positive bytes**, 805,107 scoped ops and ~8.513s exclusive Lua callback work; these are **not** included in owner-zero bytes.
- `CallList-VehicleDetailsOverlay-CET/init.lua:362–363` processes all 640,974 `OnAction` callback dispatches but immediately returns unless the minigame popup is open. This is a candidate for **behavior-preserving routing/subscription lifecycle**, but dropping its observer reduces Lua callbacks and does **not necessarily** eliminate the host's common marshalling while other `OnAction` observers remain. Verify UI activate/deactivate and all action types before changing it.
- `DriveAerialVehicle/Modules/core.lua:431–439` filters by current situation before accessing action data; direct high-frequency observer. Routing through 0-Engine is a potential callback-reduction experiment only with semantics/invalidation proof.
- `BaseMappinBaseController::UpdateRootState`: four `ObserveAfter` registrations (`DriveAerialVehicle`, `CyberTrials`, `nativeInteractions`, `sitAnywhere`) each called **74,901** times (~144/s). Their source already contains many PASS5/PASS9 guards, and some stateful mappin visuals cannot be safely skipped. `nativeInteractions/modules/utils/worldInteraction.lua:147–155` guards missing/unmatched mappins.
- `MinimapStealthMappinController::UpdateAboveBelowVerticalRelation`: `Minimap Widgets` **73,989** callbacks (~142/s), **57,541,738 scoped bytes**. Source `Minimap Widgets/init.lua:5436+` already contains PASS5 clamped early-outs. Exact cost is source-sensitive. Candidate for a focused source audit, **not** permission to memoize game-object state blindly.

### Next engineering decision

**No source rewrite or GC policy change based solely on allocations and stack grouping.** The capture *does* identify a narrow C++ producer **class** (host→Lua argument/value marshalling), but the exact triggering method targets and source-side unnecessary work remain unresolved.

Highest-yield single next experiment: correlate the already-known **host dispatch target** (`FunctionOverride::HookRunPureScriptFunction`, `HandleOverridenFunction`, `NativeProxy::Callback`) with the marshalling type `ClassReference`, `WeakReference`, `StrongReference`, `Enum` at a **bounded, capture-gated native-only diagnostic** boundary *before* Lua callback ownership. This can classify which game function targets produce wrapper traffic without global Lua stack sampling. Raise bounded sample capacity or distribute samples across time so the last ~214 seconds are represented, and retain all legacy exact reconciliations. Do **not** wrap `FunctionOverride::next()` in unsafe RAII, add Lua calls, change GC thresholds, or break CET overlay. Alternatively, source-audit the above concrete hook groups first and apply only proven state-/lifecycle-safe changes in a reversible Resolver pass.

**Gameplay safety after capture stop is unknown**, because no user post-stop behavior was included with the archive. If user confirms input/menu/sleep operations worked, record that confirmation separately.

[Parent experiment](../research/UNSCOPED-REALLOC-AND-CET-INGRESS-2026-10-14.md) · [third capture](THIRD-NATIVE-GC-DECOMPRESSION-2026-10-14.md) · [HANDOFF](../../HANDOFF/CORE.md)
