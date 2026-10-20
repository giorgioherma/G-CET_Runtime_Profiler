# Current live-stack extended dormancy intake — 2026-10-20

**Scope:** Resume the 62-mod per-mod A/B/C semantic dormancy work against `HANDOFF CHAT FILES(1).zip`. This is source/capture intake and focused source audit; no new semantic injector or runtime game validation is claimed.

## Source provenance

- Archive contains 62 original semantic mod folders, the current `modsLIVEnow` installation, three full checkpoint captures and prior passes.
- Exactly **27 of the 62** original mod identities occur in the current live installation.
- Installed PASS5 mod-file contents match live byte-for-byte: AppearanceMenuMod, AutoLoot, DriveBus, ImmersiveFirstPerson, ziggy_last_play.
- Installed PASS6/7 contents match live: GigCore, SpecterM11RecoveryDev, nativeInteractions, radioExt (outside the 62 original semantic identities).
- PASS8 DAV and Hotscenes changes match live. Immersive Third Person has the later revision matching PASS9, not the PASS8 file.
- Excluding those already handled leaves **17 live semantic owners**: BetterLootMarkers, CallList-VehicleDetailsOverlay-CET, CyberTrials, Dedka Auto Shop, JoytoysOfNightCity, Minimap Widgets, NPC Outfit Manager, QuestRunner, QuestTrackingToggle, StreetGamesPoker, illegal_mechanic_phase1a, marmurbank, repeatable_cyberpsychos, repeatable_increased_criminal_activity, sitAnywhere, tunnel_rescue, yaibaPenthouse. Many already have earlier semantic stages; this is not a claim of 17 missing optimizations.

## Checkpoint captures

| Capture | Seconds | Entire-stack Lua exclusive ms/s |
|---|---:|---:|
| Pre Lua-to-native batch baseline | 269.436 | 138.503 |
| First five modifications | 478.854 | 131.895 |
| Second batch / latest evidence | 245.952 | 138.904 |

Capture 3 adds bounded Lua-native callsite and native pre-dispatch evidence. Different durations and gameplay scenarios **prevent causal before/after performance claims** from these totals. Do not assign owner-zero native marshalling bytes to a particular mod.

## Remaining-owner priority by final capture

| Owner | Lua exclusive ms/s | Next source question |
|---|---:|---|
| NPC Outfit Manager | 4.942 | Existing no-auto-rule guard; investigate active attached-NPC radar record-ID/string churn and lifecycle/invalidation |
| tunnel_rescue | 3.011 | Preserve offered/active/recovery/cleanup; verify safe passive presentation and native getters |
| Minimap Widgets | 2.776 | Already-installed widget-locality; continuous minimap observer must stay live |
| CyberTrials | 2.588 | Installed owned-variant mappin prefilter; review separate overlay/UI draw |
| illegal_mechanic_phase1a | 2.524 | Physical NPC, interaction and hack signal independent wake; active debug rendering |
| JoytoysOfNightCity | 1.824 | Scene/bridge discovery versus active work and cleanup |
| yaibaPenthouse | 1.452 | Bath proximity plus lamp/candle OnGameAttached lifecycle |
| StreetGamesPoker | 0.972 | Existing PASS4 world scheduler complete; PokerHUD invisible native getter candidate |
| BetterLootMarkers | 0.905 | Mandatory mappin refresh and inventory freshness |
| sitAnywhere | 0.847 | Scanner/locomotion/interaction wakes |
| QuestRunner | 0.701 | Quest manager/spawn activation transition proof |
| CallList-VehicleDetailsOverlay-CET | 0.393 | ~1009Hz native OnAction; localized moveup precludes guessed exact-action names |
| QuestTrackingToggle | 0.122 | Existing route and mod-owned toggle remain |
| Dedka Auto Shop | 0.084 | Deferred physical/PC shop actions and timer wake |
| repeatable_increased_criminal_activity | 0.059 | Completion wake; verify already-optimized staging |
| repeatable_cyberpsychos | 0.038 | Defeat/wake and Mappin/diagnostics staging |
| marmurbank | ~0.001 | Keep bank interaction/deposit-exit native wake |

## First independently source-checked candidate: Poker HUD

The live `StreetGamesPoker/PokerHUD.lua` is unchanged from the pristine original. `onDraw` runs ~43/s and ~0.948 Lua-exclusive ms/s at checkpoint 3. Its `PokerHUD.draw()` invokes `PokerHUD.push(false)`, which calls `GetPlayer()` and rebuilds the invisible-state signature before checking whether that exact hidden state was already pushed. Bounded deep-function sample in this registration includes 24 `GetPlayer` calls; do not extrapolate sampled calls to total work or savings.

Proposed A/B extension **within the authoritative Poker source owner**: only when `not force`, `not PokerHUD.visible`, and the last completed signature proves the previously pushed hidden payload, avoid the redundant player getter and serialization. Must retain the initial hide delivery, player-unavailable retry, `PokerHUD.hide()` forced reset, visible/live round HUD and session handling. The existing scheduler/wake and `StreetTables.lua` stages remain unchanged. **Now implemented as source generator code and catalog opt-in, CI outcome pending; not game-tested.** Require pristine→complete single-pass, live partial upgrade, idempotence, formatting, source drift refusal and CI before declaring a stage complete.

## Other measured/source boundaries

- CyberTrials' custom racing variant is filtered in its existing Lua `BaseMappinBaseController::UpdateRootState` callback, but the remaining native observer crossing is not eliminated.
- CallList's `OnAction` uses a localized substring action selector; replacing it with hardcoded finite action names is unsafe.
- Yaiba lamp/candle attachment observers must still process source-defined object attachment events; distance-only native suppression lacks independent wake proof.
- NPC Outfit Manager `onUpdate` in checkpoint 3 is ~4.620 ms/s vs ~0.36 whole-mod in baseline, with bounded 271 `GetRecordID` and 792 `gsub` sampled function calls. The capture does not say whether auto outfits were actively assigned, nor prove a regression. Existing dormant fast path must be respected.

## Required continuation

Complete the 17-mod A/B/C source audits using the original and installed files; implement only missing source-proven stages in authoritative per-mod injectors with source-only admission where reviewed. Runtime C `SetActive` is only subscriber dormancy, not elimination of the shared CET native→Lua marshalling crossing. Preserve native listeners needed to wake, all mod-owned keys, quests, cooldowns, sessions and cleanup. Do not reintroduce deferred generic location-cache or experimental native host suppression. Update the stage matrix with proven statuses, run source/CI contracts, and separately record game validation.

**Current stage state:** intake only; no additional 17 owners marked VERIFIED and no new generated overlay.
## Initial Poker HUD implementation checkpoint — 2026-10-20

- Added the independently named `streetgamespoker-hud-invisible-native-idle` production source rule in the existing Poker source handler `PokerPass4Source.cs`. Original PASS4 controller/world generation stays unchanged; no additional native CET/0-Engine observer or new input binding.
- Added explicitly opt-in source-only admission for `StreetGamesPoker/onDraw`, with null runtime when absent from capture; the source injector requires original hide/force, last-key construction and GetPlayer-first structure. Completion is postcondition-checked, source drift fails closed.
- New Windows source-contract `tests/PokerHudInvisibleIdleContract.ps1` covers pristine one-file generation, last-key guard before getter, source-only admission with no manufactured capture owner, real existing-source idempotence, whitespace/quote variants and unknown player getter refusal.
- In local Python source-shape tests, exact original/live `PokerHUD.lua` matched, quote/comment variation matched, drift was refused, and 1,000 deterministic HUD-state sequences preserved the same native HUD write decisions. That is **not** an execution of the actual C# Resolver or an in-game benchmark.
- Fixed a CRLF-specific first-line matcher after testing against the actual original source. **Windows CI final result is not verified in this intake; no player overlay or measured saving should be claimed.**
