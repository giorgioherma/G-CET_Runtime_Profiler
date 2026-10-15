# Native `OnAction` allocation pressure → Resolver outgoing metric (2026-10-15)

## Root cause of the measurement mismatch

This extended-dormancy investigation was triggered by **LuaJIT allocator growth during native-to-Lua argument marshalling**, not callback-exclusive Lua CPU. A low/zero `exclusiveMsPerSecond` does not prove that delivering a game native event to an inactive observer is free.

The user's newer `CET-20261015-000909_ONACTION_DISCOVERY_IMMERSIVE_AND2` capture includes **1,074** bounded native samples. Of these, **599** carry the exact method hash `9658066991423262154` (`OnAction`, FNV-1a 64-bit) at `CET_FUNCTION_OVERRIDE_PURE`. The entire `CET_FUNCTION_OVERRIDE_PURE` **unscoped** boundary recorded **182,015,438 positive bytes** and **1,057,771 allocator operations**. **Do not equate these figures:** the first is a bounded sample count; the second is the exact population of that entire boundary, spanning multiple methods. Neither identifies a mod or proven avoidable allocations. The capture did not record an RTTI class.

## Actual instrumentation and outputs

**Native experimental source (no production DLL promotion):**
- `native-profiler-build/v2.11.0/Patch-CET-v1.37.1.ps1`: at both FunctionOverride argument-marshalling boundaries, obtain `apFunction->parent->GetName().hash` **only while a capture is active**. Pinned CET/RED4ext SDK identifies `apFunction` as `RED4ext::CClassFunction*` and its `parent` as `RED4ext::CClass*`; `CClass::GetName()` returns CName with a numeric hash. NativeProxy carries `classHash=0` because its function pointer is not proven to have that layout. No reinterpret_cast, global RTTI registry or mod ownership inference.
- `CETRuntimeProfiler.h`: `MethodScope` saves/restores both method/class TLS tags for nested calls. The native sample struct appends a `uint64_t classHash`; `CET_Runtime_Profile_GGC_UnscopedNativeSamples.csv` appends `NativeClassHash` without changing previous columns or population counters.
- `Patch-LuaJIT-GC-Source.ps1`: snapshots the already-captured TLS class hash **only in the existing bounded per-second allocator sample slot**. No RTTI, string, Lua API, heap allocation, or stack-unwinding expansion on each allocation. Capture-OFF does not evaluate the class GetName call.
- This native ABI change must be compiled, independently validated and hash-pinned before any later product DLL promotion. CI compilation alone is not proof of in-game stability. Existing main binary remains pinned/unchanged.

**Managed report and handoff:** `ResultReportService.NativeMarshalling.cs` emits `nativeMarshallingAllocationPressure` in **both `CET_Summary.json` and `CET_Resolver_Input.json`**. This metric includes:
- Named scope `NATIVE_TO_LUA_ARGUMENT_MARSHALLING_ALLOCATION_PRESSURE`, `nativeMethodHashRecorded`, `nativeClassHashRecorded`, sampled/dropped counts, and bounded **method+class+native-boundary** groupings (hashes serialized as unsigned decimal **strings** to preserve 64-bit precision).
- `onAction.observedMethodSampleCount`, `observedSamplePositiveBytes`, `observedClassQualifiedSampleCount` and, only for a matching known `PlayerPuppet` RTTI hash, `observedPlayerPuppetSampleCount`.
- Independently reconciled `nativeBoundaryExactOwnerZero` buckets: exact **outer boundary population** only, never treated as `OnAction` cost or summed with sample bytes.
- For legacy native CSV with `NativeMethodHash` only, method samples remain eligible for *review*, but class identity is `NOT_RECORDED_LEGACY`. With no method column or missing native samples the metric is unavailable/unknown rather than fabricated.
- HTML shows bounded OnAction marshalling pressure separate from CPU and owner totals.

**Resolver:** `NativeMarshallingEvidenceReader` consumes the new handoff field and the report includes its original full evidence in `nativeMarshallingAllocationPressure`, plus `nativeAllocationPressurePolicy` and `summary.nativeOnActionMethodSamples` / `nativePlayerPuppetOnActionSamples`. Each callback's existing `runtime.nativeDispatch` now names this **separate outgoing metric**, and `sharedPlayerPuppetOnActionNativeSamples` only when the target is exactly `PlayerPuppet::OnAction`. Its `observedNativeBytesAttributedToThisSubscriber` remains **null** and its admission logic is unchanged. A class-qualified shared native envelope still cannot identify which mod's Lua observer produced an allocation or the exact number of native allocations avoided after optimization.

## Decision path and acceptance criteria

- Native marshalling metrics rank **research pressure**, not automatically executable mod-specific transformations. Generic action routing still requires exact source registration, action selector and fallback proof. A user-approved semantic rule can remain eligible below 3 ms/s with source-safe generation.
- A real before/after native saving claim requires **matched gameplay state and duration, class+method+boun­dary sample changes, boundary population/callback dispatch counts, and full sample coverage**, with workload differences and sampling bias made explicit.
- New `tests/NativeMarshallingResolverContract.ps1`: class-qualified, legacy method-only, missing metric, and per-mod attribution exclusion.
- Expanded `tests/UnscopedAttributionReportContract.ps1`: legacy unqualified, method-only and class-qualified FNV hash, exact preserved counters, HTML and shared handoff evidence.
- The next experimental native capture should determine whether the dominant `OnAction` samples really have `PlayerPuppet` as their RTTI class and how many were collected at pure override vs native proxy. Current previous capture cannot provide that class retrospectively.

## Source-of-truth and release constraint

Use [native-method research](NATIVE-MARSHALLING-METHOD-AND-TIME-SAMPLING-2026-10-14.md), [Resolver full dormancy audit](../../resolver/DORMANCY-ALL-MOD-AUDIT.md), and `docs/HANDOFF/CORE.md`. Product version stays **1.2.7**. No 0-Engine, mods or gameplay behavior changes. Native replacement remains pending independent native CI, game-load safety, capture-off tests, matching PDB, pinned SHA and deliberate promotion.
