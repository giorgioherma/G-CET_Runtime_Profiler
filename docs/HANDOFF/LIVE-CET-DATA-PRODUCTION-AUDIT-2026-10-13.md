# Live CET source audit — 2026-10-13

**Status: static source inventory completed; targeted source candidate analysis completed; no mod/Resolver modifications.** Full local companion outputs: `CET_Live_Data_Production_Audit_2026-10-13.md` (123 source-informed report paragraphs/rows), `CET_Live_Source_Inventory_2026-10-13.csv` (per-mod census), and `CET_Live_Source_Hooks_2026-10-13.csv` (25,382 source-occurrence rows). User-uploaded `mods(3).zip` is the live collection and `CET-20261013-031413_CONTAIN_THE_DATA(1).zip` is the v1.2.5 capture.

## Coverage improvement and limitations

The new live ZIP has **138 mod directories, 1,550 Lua source files**, unlike previous incomplete pass subset of 87 mod directories and 164 files. Measured capture has 139 named owners; `CETProfilerControls` is not a separate mod directory in the ZIP. Every Lua source file was scanned for registration and data-production patterns; detailed control-flow and lifetime proof was performed only for shortlisted sites. Do **not** claim this is full semantic execution-path validation of 1,550 files. Counts include possible comments, libraries, and cold paths.

Matches: 7,591 `{}`, 5,049 `pairs/ipairs`, 4,677 `tostring`, 2,357 `Observe`, 1,292 `string.format`, 917 `GetQuestsSystem`, 814 `Game.GetPlayer`, 671 Cron calls, 483 `GetWorldPosition`, 388 `GetEntityID`, 362 `Override`, 268 `ObserveAfter`, 168 `FindEntityByID`, 134 `GetMappinSystem`, and fewer other sites. Static matches are **not allocation, execution or optimization counts**.

Actual 698.202s capture context and 592s timeline cap/late DROP/STOP caveat remain as recorded in `GC-RESEARCH.md`. G-GC whole-recording positive 2,042.068MiB / 16.434M requests; owner0 unscoped 764.241MiB / 5.230M requests; 19,834.020ms native collector NON-ADDITIVE. No exact per-owner before intended stop is possible.

## Ranked source-level investigation targets

1. **NPC Outfit Manager** — `init.lua:529–540` `GetNPCID` native `GetRecordID()` + string `match/gsub/tostring`; `1820–1846` scans all attached `activeNPCs` once/s and repeats normalization; many other callers; `5459–5473` has separate frame-responsive look-at tracking. Consider per-entity-generation validated normalized-key memo only after detach/save/load/NCA/appearance/ID reuse/alias proofs. Allocation impact unmeasured; source repetition proven; medium gameplay risk.
2. **licksclubjukebox** — `init.lua:71–93` photo-state blackboard read + Cron + per-frame controller dictionary sweep; every entry creates `EntityID.new({hash=key})` and invokes `Game.FindEntityByID` for liveness. Investigate bounding only stale cleanup while preserving immediate photo mute, invalid entity teardown and Cron. Native call expense not separately measured.
3. **BetterLootMarkers × AutoLoot** — `BetterLootMarkers/init.lua:77–123` uses controller `GetMappin/GetVariant`, target `FindEntityByID`, `GetItemListForObject`, `ResolveHighestQualityByCategory`. AutoLoot has its own live optimized early-radius and inventory work. Cross-mod item-list equality, freshness, mutation ownership and marker invalidation **NOT MEASURED**; instrumentation candidate only. Don't worsen marker delays.
4. **BaseMappinBaseController observers** — multiple mods (CyberTrials, nativeInteractions, DriveAerialVehicle, sitAnywhere) share the native event; event-local scalar `GetMappin/GetVariant` overlap worth studying but existing identity/variant-first guards must remain. No global controller native userdata caches, no callback-order changes.
5. **yaibaPenthouse** — `init.lua:105–124` already shares player position with bathtub; candleLights/lampShades/marker lifecycle independent. Full source includes other observer paths; measured owner 4.315ms/s and 1306.5 calls/s. Do NOT re-propose already-present player position sharing.
6. **SpecterB34S7Dev** — `init.lua:17–19` dispatches three developer module updates through pcall each frame; capture owner ~1292 calls/s but only 1.304ms/s. No cadence modification without proof. `SpecterM11RecoveryDev/init.lua:707–739` inactive polling is coupled to immediate M9→M10 handoff; don't delay it blindly.
7. **0-Engine / GigCore / ImmersiveFirstPerson** — large scoped time does not identify allocation producers. Existing router, guarded work, active camera and session safety require preserving. Investigate specific producer callsites/value probes instead.

## Implementation plan (no optimization authorized yet)

- Join the **59,959 sampled Deep_Callsites** rows and **8,314 Deep_Functions** rows by registration/epoch/function key to rank actual source-located operations; retain sampling/dropped coverage and no owner0 attribution.
- Add source-in-situ, opt-in producer/parameter/result probes for NPC record keys, controller liveness, item-list requests and same-event mappin scalar reads; **never make extra getters on hot paths**.
- Prove no escaping mutable lists, query parameter equivalence, session/receiver identity, event ordering, immediate wake and invalidation BEFORE proposing data sharing.
- Candidate generic Resolver rules only for trivially proven no-escape redundant materialization / repeated immutable per-invocation scalar work. Source-specific semantic work for NPC cache, controller liveness or quest lifecycle only after measured payoff. Reuse existing generic→semantic composition; maintain idempotence and source variants.
- Preserve v2.15 native production SHA and every CPU, GC, burst and timing field. No mod, Resolver or native files changed by this audit. Next run needs explicit stop acknowledgments / time-binned allocation producer evidence, not just a better score.

## Shortlist status

All seven items are **investigations**, not automatic passes; there is **no measured exact MiB savings** and **no established cross-mod equivalent-value group**. Source metadata and complete local CSV inventory should travel with future chat if exhaustive line-by-line inspection is needed. See `CET-CALLBACK-AUDIT-2026-10-13.md` for the earlier partial source analysis and current capture correction.
