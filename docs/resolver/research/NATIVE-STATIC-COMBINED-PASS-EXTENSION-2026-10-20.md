# Shared native static-location dormancy — combined-pass extension (2026-10-20)

**Authority:** user-supplied `ALL passes combined-final state-base for locational dormancy.zip`, containing the already-researched, combined optimized Lua sources. This extension copies the existing source-owned dormancy boundaries; it does not substitute new generic quest or NPC policy.

## Native-backed coarse owners (additional)

| Owner | Native id | Source-owned location and existing dormant rule | Native-owned work | Exact author work retained |
| --- | ---: | --- | --- | --- |
| Card Collection | 10035 | `Card Collection/init.lua` static `buildSpatialEntries()` over **100 cards minus those already picked**; existing 0-Engine `RegisterSpatialSet` uses 2 m radius, 1 s collection poll | Native aggregated 20 m NEAR/FAR parks the original spatial set while every uncollected site is distant; recreates it on NEAR | Original card collection, fact writes, rewards, `RemoveSpatialEntry`, PlayerReady/Invalidated lifecycle, and polling callback |
| MarmurBank | 10036 | `marmurbank/module/Bank.lua` uses `PosData.All` (three static banker positions), `RegisterSpatialSet` and `QueryNearest` against original exact 2 m banker radius | Native aggregate 10 m FAR retires spatial index and bypasses dormant nearest query | Existing timer/UI, 2 m exact nearest banker selection, menus, balance/interest, and source-known recovery path |
| Yaiba Penthouse bathtub | 10037 | `yaibaPenthouse/bathtub.lua` already has `G-CET semantic:yaiba-penthouse-distant-bathtub`, early return only with no hub, pin or pending water state | Native FAR replaces the old square-distance calculation while that identical inactive predicate holds | Original state predicate, 6 m icon and 1.8 m interaction ranges, water/mappin cleanup, forward-facing angle and original source calculation during native UNKNOWN |

For the bathtub the source distance uses the player's eye position `pPos.z + 1`; the native site is consequently anchored at `CONFIG.position.z - 1` to preserve this coordinate frame. The coarse 20 m card and 10 m banker native wake radii are **conservative supersets** of the exact original 2 m interaction radii. Card Collection's existing 1 s poll needs an advance wake before a player runs through a collectible; the original exact collection boundary remains unchanged.

All three call the existing shared `0-Engine.GCET.NativeLocationHost:attach`. No additional 10 Hz timer, native DLL, generated static manifest, or per-mod native daemon is introduced. The existing `GCETStaticLocations.dll` already allows runtime source-owned anchors and accommodates 100 entries. A missing/UNKNOWN/overrun native state keeps or restores the original Lua behavior; no source SHA lock is added.

## Eighth owner: Ziggy — existing offered-state 130 m gate

`ziggy_last_play/init.lua` already implements a source-proven
`__gcetZiggyOfferedFar()` early probe: only an unaccepted/offered,
correctly attached player with a valid session, matching identity/generation,
and no ongoing dialogue can be considered FAR when squared distance to the
fixed `C.giver.position` exceeds `130*130`. In that case the existing
controller is advanced **once per second** using accumulated `dt`. All
nearby and active quest phases remain on the original frame cadence.

Native owner **10038** registers precisely `C.giver.position` at **130 m**
through the existing shared host. The old game's position read and
squared-distance calculation are replaced with native NEAR/FAR **after**
the original phase/session/player/dialogue guards; native UNKNOWN/failure
falls through to the old exact source calculation. Journal, fighter
spawns, pickup, listening, recoveries, input and UI are unchanged.
No broad quest-phase assumptions or generated coordinates are introduced.
The source-only Resolver rule `native-location-ziggy-offered` upgrades
the already-optimized combined Lua; its native subscription shares the
existing single 10 Hz host with the other seven owners.

### Quest Runner remains on its existing rule

`QuestRunner/init.lua` already applies the reviewed PASS5 rule:
`Manager:update` runs at 5 Hz while no quest is active and immediately
returns to frame updates for an active quest; `Spawner.Update` receives
time-accumulated updates at 4 Hz while maintaining its authored 1 s
internal scan. `module/Manager.lua` determines available quest triggers
via the dynamic `quest:isDoable()` callbacks, **not fixed map coordinates**.
`module/Spawner.lua` handles quest-owned, mutable `wnpc.pos` plus
`spawnPos`, NPC lifetime, player view angle and spawn-distance thresholds.
There is no single source-proven static-location FAR gate to copy onto
the whole framework. A native FAR assumption here could miss triggers or
delete/spawn actors incorrectly. The existing PASS5 scheduling and
`Spawner` semantics are unchanged.

## Scope not translated to static anchors

Dynamic `CourierJobs` pickup/delivery destinations, `Hidden Packages` swappable map/collection contents, active `ziggy_last_play` fight/listening quest positions, and live actor/object proximity used for UI or spawning are **not copied** into immutable native anchors. Their current optimized rules continue unchanged. The same holds for generic 0-Engine `IsNear` consumers without an existing source-proven coarse idle condition; they must not be falsely made permanently FAR.

## Contracts and validation

- Resolver source injectors: `NativeLocationAdditionalOwnersSource.cs`.
- Catalog: `native-location-card-collection`, `native-location-marmurbank`, `native-location-yaiba-bathtub`.
- Source-level regression: `NativeLocationAdditionalOwnerContract.ps1` (author source shape, LF/CRLF, original callback retention, idempotence).
- Shared Lua host regression now includes **all eight owners**, including the complete 100-card native anchor snapshot with **one** consumer.
- Native branch binary remains unchanged, and combined runtime gameplay testing is still required for save/reload, fast travel, source fallback, and actively collected/visible states. CI validates code and generated behavior contracts, not an in-game playthrough.
