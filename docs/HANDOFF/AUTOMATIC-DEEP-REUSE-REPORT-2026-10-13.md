# Automatic deep data-reuse investigation in reports (2026-10-13)

## Implementation

The normal managed `ResultReportService.Generate(captureRoot)` now reads native v2.15 `CET_Runtime_Profile_Deep_Functions.csv`, `Deep_Callsites.csv`, and `Deep_Registrations.csv` **without extra game hooks or external Python**. It automatically adds `deepDataReuseCandidates` to both `CET_Summary.json` and `CET_Resolver_Input.json`, plus a human table under **Data reuse investigation: sampled producers and callers** in `CET_Report.html`.

- New analyzer: `src/G.CETProfiler.Core/Services/ResultReportService.DeepReuse.cs`.
- Existing report paths wired in `ResultReportService.cs`, `ResultReportService.Analysis.cs`, `ResultReportService.Resolver.cs`, and `ResultReportService.Html.cs`.
- Regression contract: `tests/DeepReuseReportContract.ps1`, invoked from Windows CI `.github/workflows/build.yml`.
- Older captures missing deep CSVs explicitly show `NOT_CAPTURED` and preserve all previous JSON and metrics.
- Original Python CLI remains an optional research utility; it is **not** required for capture or report generation.

## Source attribution semantics

Callsite → sampled child function join uses **(`RegistrationId`, `ProfileEpoch`, `ChildFunctionKey = FunctionKey`)**. Owner/mod names use stable registration ID because registration metadata profile epoch may differ. Ambiguous function-key collisions and conflicting owner IDs are excluded from ownership claims and surfaced in coverage.

For each function we report total native sampled function-own positive allocation bytes/operations **once**. Each caller has its own sampled invocation-edge calls, frames, and invocation ordinals, but **no allocated bytes assigned to an individual caller**. Samples remain bounded, nested inclusive timings are not savings, and owner-0 is not attributed. We do not equate positive allocator growth with garbage or retained heap.

The report contains candidate lists sorted by sampled function own allocations and by sampled invocation edges. Cross-mod data equivalence, actual table-content update frequency, receiver/input equality, mutation/escape ownership, freshness and invalidation remain **NOT_MEASURED / NOT_PROVEN**; all candidates are `INVESTIGATE_ONLY` with `safeToShare=false`, `estimatedAvoidableBytes=null` and `authorizesAutomaticRewrite=false`. **Zero proven shared-cache groups.** This stage is proof collection only, not new mod/Resolver transformations.

## CI / user test

Windows CI exercises legacy reports and a fixture with multi-caller one-producer joins, duplicate function IDs, nonmatching sample epochs, registration metadata epoch drift, and no double-counting. Review the latest workflow result before shipping.

For an already extracted capture, generate reports using the portable Profiler app: `app\G-CET-Runtime-Profiler.App.exe --report --capture "C:\path\to\capture" --json`. A new normal capture report includes the section automatically. No extra Python installation, profiler DLL replacement or changes to current CET mods.

## Following engineering stage

Source-validated selected in-situ data probes (only existing getter calls), actual immutable result/parameter stability, writer/invalidation/mutable alias proofs and distinct cross-mod consumers. Do not auto-share native tables or global-cache by API name. Native 2.15 payload SHA remains pinned and previously crashing native 2.16 is not promoted.


## User-reprocessed 698.202-second capture validation — 2026-10-13

User supplied **CET-20261013-031413_CONTAIN_THE_DATA(5).zip**, regenerated at `2026-10-13T20:31:45Z`. Confirmed the new HTML section and both `CET_Summary.json` / `CET_Resolver_Input.json` contain `deepDataReuseCandidates` with `status=BOUNDED_SAMPLE_ANALYZED`. Deep coverage: **8,314** function rows, **59,959 / 59,959** matching scoped callsite rows, **4,454** function/epoch identities with caller evidence; zero unmatched callsites, duplicate scoped function keys, or conflicting owner registration IDs. Function-own sampled allocations counted once: **30,265 positive allocator requests**, **3,421,841 sampled positive bytes**. These are bounded source samples (NOT the capture-wide >2 GiB positive allocation growth), not proven garbage or savings. The simultaneous legacy `gcTracer` still records all 2,278 G-GC function rows as `NOT_RECORDED_LEGACY`; that is expected and distinct from the usable deep callsite evidence.

### Source-checked next investigative targets

1. **NPC Outfit Manager per-attached-NPC ID normalization**. `mods/NPC Outfit Manager/init.lua:529-540` gets a record ID, converts it to a string and invokes `find`, `match`, trim `gsub` operations; `ProcessRadarOutfits` at `1820-1845` calls `GetNPCID(npc)` on every tracked attached NPC scan. Deep sample records **598** calls to `GetNPCID` at `init.lua:1826`, **598** `GetRecordID` calls at `531`, **1,140** `gsub` invocations at `536`. Candidate for a validated per-entity/record-generation normalized ID cache, *only after* checking record ID stability, NPC reuse/despawn, NCA compatibility, appearance swaps and reset points. Strings, not tables.
2. **Player state scalar blackboard overlap**. `mods/0-Engine/modules/BlackboardCache2.lua:193-204` already reads **11 different GetInt fields plus one GetBool** in a single batch and skips derivation when values do not change; **do not claim that it lacks a cache**. `ImmersiveFirstPerson/Modules/Helpers.lua:161-223` also reads overlapping PSM fields (`LocomotionDetailed`, `Landing`, `SceneTier`, `Weapon`, `UpperBody`, `Swimming`, `Takedown`, `BodyCarrying`). Deep sampling sees **648 GetInt** calls at `Helpers.lua:145`. Potential cross-mod **scalar snapshot** experiment only if the same blackboard instance/generation, definition field identity, frame consistency and response requirements are proven. Current capture does **not** measure value changes and the mods already possess separate caching/session controls.
3. **Loot inventory list overlap**. `mods/BetterLootMarkers/init.lua:111-122` queries target loot via `GetItemListForObject`; `AutoLoot/modules/logic.lua` independently queries inventory as part of its protected transfer path. Native sample records **24** `BetterLootMarkers GetItemList` calls and **13** `AutoLoot GetItemList` calls. No evidence they used the same entity, equal contents, compatible freshness, or safe mutable ownership. Transfers and marker updates can invalidate immediately. Do not share arrays until source and selected bounded result evidence prove a contract.
4. **GigCore bus getter is not an obvious persistent-handle cache**. `mods/GigCore/core/bus.lua:18-24` deliberately fetches `GetScriptableSystemsContainer():Get(BUS_ID)` fresh and guarded so the REDscript companion can be unavailable. Capture deep edges do not authorize changing this behavior; investigate narrowly with session/generation guards if necessary.

### Current analyzer interpretation / report improvement

The top sampled-function rows are dominated by native `__index` operations, which **are not proof of actual table production or result equivalence**. The report should separate `GENERIC_RUNTIME_DISPATCH` (e.g. `__index`) from `SCALAR_READ`, `DERIVED_STRING`, `COLLECTION_QUERY`, `TABLE_CONSTRUCTION`, and `UNKNOWN` investigative lanes, without discarding raw evidence or declaring reuse. Many repeated function names also occur in **separate ProfileEpochs**, correctly represented by distinct measurement identities; don't label their aggregate costs as one caller or one reusable producer without further evidence.

**Next native experiment**, permitted by the user: **separate opt-in/capture-gated build**, not an immediate release-DLL swap; minimally sample existing in-situ producer *inputs and scalar return signatures*, observed equality versus changes, stable field key/generation, and source callsite. Bound events and hashing/copy sizes, no extra game getter invocations, no mutable native pointer lifetime extension, no global interception or arbitrary table serialization. Separately improve owner-0 execution context provenance. Preserve known-good 2.15 release artifact until startup before F11, identical save, on/off, stop/pause, session transition, conflicts, overhead and long-play stability are validated.



## User-directed prioritization: idle NPC scanner first; shared loot view second (2026-10-13)

User clarified that **NPC Outfit Manager's editor was never opened in the test** and it should not continually create work while idle. User also prioritizes a **shared cached loot view for BetterLootMarkers and AutoLoot**; 0-Engine / ImmersiveFirstPerson cross-mod state cache is less certain and should remain lower priority.

### Confirmed author-code cause: NPC Outfit Manager

Read live `mods(3).zip` source `mods/NPC Outfit Manager/init.lua`:
- `onUpdate` **always** invokes `GetLookAtNPC()` at **5460**. Its implementation **1304-1325** runs `Game.GetTargetingSystem():GetLookAtObject(player)` and checks a remembered NPC's `IsAttached()`.
- **5469-5472** runs `ProcessRadarOutfits()` **once per second regardless of whether there are any auto-outfit rules**.
- **1820-1845** loops `activeNPCs`, calls `GetNPCID(npc)` on each attached NPC, then only afterwards tests `autoOutfitsData.outfits[lookupId]` for a matching rule.
- **529-540** performs `GetRecordID`, `tostring`, `find`, `match`, `gsub`. These calls explain the observed sampled record-ID/string churn. `OnGameAttached` and `OnGameDetached` observers **1871-1891** maintain the NPC list even with no rules.
- `autoOutfitsData.outfits` defaults to an empty table at **160**, is loaded at **1157-1166** from `npc_auto_outfits.json`, and may be changed *live while the UI is open* at **2453-2467**. The user archive does not include the active `npc_auto_outfits.json` state; verify zero active assignments in an actual installation before claiming zero-rule measurements. The mod uses automatic outfitting in the background by design: **UI closed** alone does NOT mean this subsystem is unused.

**Safe first optimization candidate:** when the auto-outfit rules table is empty, bypass the periodic `ProcessRadarOutfits` scan; skip continuous `GetLookAtNPC` polling unless a UI/camera/animation/pending targeting feature explicitly needs it. Preserve `Cron.Update`, pending wardrobe/camera actions, on-demand editor access, and ability to set an auto outfit while playing without restart. Do **not** unregister lifecycle observers blindly or drop already attached targets on re-enable. If rules are nonempty, follow with per-entity normalized NPC ID caching keyed by safe entity/record generation and cleared on detach, load and appearance/record changes; include NCA mapping rules. Tests: no rules + UI closed, editor opens, automatic assignment activated live, NCA, attach/detach/respawn, applied-once logic, camera and Cron pending work. This is an evidence-backed *source optimization candidate*, not a committed mod patch.

### Source-checked loot-sharing boundary

- `BetterLootMarkers/init.lua:111-122`: on mappin `UpdateVisibility`/`UpdateIcon` it queries a target via `GetItemListForObject` and constructs quality/category/count aggregates at **188-212**. That list helper at **243-262** reads the **object**, falls back to `owner` **only when object list is empty AND owner is `itemDropObject`**.
- `AutoLoot/modules/logic.lua:1595-1635`: on accepted loot action it queries object and owner inventories separately, then `lootObjectItems` at **1929 onward** reads **fresh inventory** before protected item selection/transfers. It also checks remaining NPC loot when hiding mappins at **1550-1575**.
- There is meaningful overlap in the underlying game transaction system but **different object vs owner routing, staleness tolerance, and consumer requirements**. An immutable `entity identity + generation + inventory revision -> marker category/quality/count summary` can let multiple marker refresh callbacks reuse presentation data; AutoLoot should read fresh item handles for actual quest/shard/protection and transfer decisions. AutoLoot can **invalidate** relevant marker snapshot entries upon successful/partial changes; other inventory mutation paths (manual loot, game grants, despawn/respawn, owner change, save/load) require their own invalidation or bounded conservative refresh. Without a reliable change signal, expiry is only a fallback and stale markers are a correctness risk. Don't share mutable native `gameItemData` arrays or claim the current 24/13 sampled calls prove savings. A provider must be optional/load-order-safe, with original fallback if other mod absent.
- Focus a separate experiment on repeated `UpdateVisibility`/`UpdateIcon` for the **same entity** within a stable change generation and on source-proven inventory invalidation. Distinguish marker display reuse from actual AutoLoot inventory reads.

**Scope:** These are investigative priorities and acceptance criteria only; no mod source, Resolver semantic, or native tracer is changed by this documentation entry.


## User requirement — event-driven last-edited NPC outfit state cache (2026-10-13)

User requests **a cached latest outfit state updated whenever any outfit/appearance/body editing action occurs**, rather than continually rereading reconstructable state while the editor is not used. This is **distinct** from the automatic outfit radar cache and does not authorize overwriting an explicitly saved outfit preset.

Source evidence from the exact live `mods(3).zip` (`mods/NPC Outfit Manager/init.lua`):
- **Existing live state tables already exist**: `currentSelections[targetHash]`, `bodyPartStates[targetHash]`, `activeRefits[targetHash]`, `activeBodyTypes[targetHash]`, `equippedSavedMeshes[targetHash]`, `nativeMeshHiddenStates[targetHash]`, `hiddenRefits[targetHash]`, plus hidden/locked slots. Avoid introducing redundant state variables when these can be canonical.
- `ExecuteSaveOutfit` at **1896-1925** explicitly serializes a comprehensive current look to `savedLooks[targetHash][slot]` and calls `SaveLooks()`, which writes `npc_saved_looks.json` at **1176-1181**. This happens on explicit save/checkout, **not each edit**. `npc_auto_outfits.json` at **1159-1174** persists the *assigned preset index*, not a current live snapshot; it is saved when auto outfit is assigned at **2450-2467**. Do not confuse saved presets, assigned auto looks, and current live appearance.
- `ApplyLookByIndex` **1620-1816** resets multiple state tables and applies changes with multiple `Cron.After` delayed continuations; **never mark full appearance as committed before the delayed state has actually applied**. Capture/apply revision should be generation-guarded against stale delayed completions.
- On every edit affecting a slot, mesh, refit, body toggle, base appearance, hair, lock, reset or look application, update the **already-tracked in-memory last-edit snapshot** (or maintain a lightweight dirty/revision marker and construct the snapshot only when a consumer needs it). Do not query the entire NPC or allocate a full copy per onUpdate / ImGui draw. Store only plain immutable IDs, booleans and strings; no live native target/item/component pointers. Manual on-demand save can re-use the snapshot after ensuring all pending operations are complete.
- If **persistent autosave of the last edit** is desired, write a **separate per-NPC last-edited working state** (not `savedLooks` preset index) on debounced/coalesced mutations or close/game-state save, and only when dirty; avoid JSON disk writes per click/slider tick. Explicit presets remain intentional checkpoints. Autosave restore must be optional or narrowly scoped to edited NPC/record and must not apply saved customization to unrelated spawned NPCs or generic archetypes. Stable identity policy distinguishes `GetNPCID` record archetype from ephemeral `GetEntityID().hash` instance and handles attach/detach, load/respawn, preset deletion, NCA compatibility and appearance changes.
- **Optimization fast path**: editor closed + no rule assigned + no pending apply/camera/animation/checkout/preview -> no per-frame target polling, no full appearance/slot reads, no radar record-ID normalization. On editor entry or an actual mutation/attachment requiring auto outfit, use canonical cached state with guarded validation/invalidation, then update cached snapshot after the operation commits.
- Tests should include edit slot/mesh/body/hair/base appearance; rapid sequential edits; deferred `Cron.After` changes and canceled game load; shopping rollback on close; apply look vs explicit preset save; toggling auto outfit live; generic NPCs with same record ID; detach/respawn; no active rules/closed UI; and no premature persistence while features are not used.

**Status: design/acceptance requirement only.** No shipped mod/Resolver semantic/native code changed in this entry; the user's current capture has not established specific recoverable MiB.
