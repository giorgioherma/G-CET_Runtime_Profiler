# Shared native static-location dormancy — combined-pass extension (2026-10-20)

**Authority:** user-supplied `ALL passes combined-final state-base for locational dormancy.zip`, containing the already-researched, combined optimized Lua sources. This extension copies the existing source-owned dormancy boundaries; it does not substitute new generic quest or NPC policy.

## Native-backed coarse owners (additional)

| Owner | Native id | Source-owned location and existing dormant rule | Native-owned work | Exact author work retained |
| --- | ---: | --- | --- | --- |
| Card Collection | 10035 | `Card Collection/init.lua` static `buildSpatialEntries()` over **100 cards minus those already picked**; existing 0-Engine `RegisterSpatialSet` uses 2 m radius, 1 s collection poll | Native aggregated 2.25 m NEAR/FAR parks the original spatial set while every uncollected site is distant; recreates it on NEAR | Original card collection, fact writes, rewards, `RemoveSpatialEntry`, PlayerReady/Invalidated lifecycle, and polling callback |
| MarmurBank | 10036 | `marmurbank/module/Bank.lua` uses `PosData.All` (three static banker positions), `RegisterSpatialSet` and `QueryNearest` against original exact 2 m banker radius | Native aggregate 2.25 m FAR retires spatial index and bypasses dormant nearest query | Existing timer/UI, 2 m exact nearest banker selection, menus, balance/interest, and source-known recovery path |
| Yaiba Penthouse bathtub | 10037 | `yaibaPenthouse/bathtub.lua` already has `G-CET semantic:yaiba-penthouse-distant-bathtub`, early return only with no hub, pin or pending water state | Native FAR replaces the old square-distance calculation while that identical inactive predicate holds | Original state predicate, 6 m icon and 1.8 m interaction ranges, water/mappin cleanup, forward-facing angle and original source calculation during native UNKNOWN |

For the bathtub the source distance uses the player's eye position `pPos.z + 1`; the native site is consequently anchored at `CONFIG.position.z - 1` to preserve this coordinate frame. The coarse 2.25 m radii for cards/bankers are a **conservative superset** of the exact source 2 m range, not a narrowed interaction threshold.

All three call the existing shared `0-Engine.GCET.NativeLocationHost:attach`. No additional 10 Hz timer, native DLL, generated static manifest, or per-mod native daemon is introduced. The existing `GCETStaticLocations.dll` already allows runtime source-owned anchors and accommodates 100 entries. A missing/UNKNOWN/overrun native state keeps or restores the original Lua behavior; no source SHA lock is added.

## Scope not translated to static anchors

Dynamic `CourierJobs` pickup/delivery destinations, `Hidden Packages` swappable map/collection contents, active `ziggy_last_play` quest positions, and live actor/object proximity used for UI or spawning are **not copied** into immutable native anchors. Their current optimized rules continue unchanged. The same holds for generic 0-Engine `IsNear` consumers without an existing source-proven coarse idle condition; they must not be falsely made permanently FAR.

## Contracts and validation

- Resolver source injectors: `NativeLocationAdditionalOwnersSource.cs`.
- Catalog: `native-location-card-collection`, `native-location-marmurbank`, `native-location-yaiba-bathtub`.
- Source-level regression: `NativeLocationAdditionalOwnerContract.ps1` (author source shape, LF/CRLF, original callback retention, idempotence).
- Shared Lua host regression now includes **all seven owners**, including the complete 100-card native anchor snapshot with **one** consumer.
- Native branch binary remains unchanged, and combined runtime gameplay testing is still required for save/reload, fast travel, source fallback, and actively collected/visible states. CI validates code and generated behavior contracts, not an in-game playthrough.
