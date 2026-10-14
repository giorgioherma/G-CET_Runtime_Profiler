# Minimap Widgets stealth elevation — measured producer-side optimization

**Date:** 2026-10-14  
**Status:** Resolver production rule `minimap-stealth-widget-locality` committed with negative/idempotence/format/source safety CI contract. Gameplay A/B not performed. Profiler/Resolver version **v1.2.7** unchanged.

## Why this optimization was selected

Fourth capture `CET-20261014-170941_FOURTH_G-GC_CAPTURE.zip`, user live mod source `mods(5).zip`:

- Measured **73,989** `MinimapStealthMappinController::UpdateAboveBelowVerticalRelation` `ObserveAfter` calls during 519.833s (~**142.332/s**).
- **2,952.530ms** exclusive Lua callback time (~**5.679764ms/s**).
- **57,541,738 scoped positive LuaJIT allocation bytes** (not retained-heap delta and not claimed to be produced wholly by property rereads).
- Exact native PDB analysis from this capture shows CET `Converter::ToLua`, userdata `WeakReference`/`StrongReference` and `Enum` construction dominate the bounded sampled native marshalling stack families. That is **producer-class context**, not causal proof for individual Minimap Widget reads.
- Author's live code has prior PASS5 clamped-branch optimization. This rule **does not repeat it or change cadence**.

## Actual source transformation

In the one measured callback only, after the author's `IsDefined(this)` guard:

1. Read `this.aboveWidget` and `this.belowWidget` exactly **once each**, store references in invocation-local variables `__gcetAboveWidget`, `__gcetBelowWidget` (no persistent cache).
2. Return immediately if both are absent; the original callback had no UI writes in that case, but performed some native state getters when the elevation option was enabled.
3. Replace all subsequent callback-local widget field lookups with those locals.
4. Leave `MinimapWidgetsConfig.ShowElevationArrow`, clamped/unclamped decisions, `GetVerticalRelationToPlayer` when needed, and **all existing SetVisible calls/arguments and refresh frequency** intact.

This removes **12 duplicate source property access expressions** from the existing callback body; executed native getter savings depend on branch and widget presence. **No FPS/GC decrease or byte savings have been proven** until in-game A/B. It is a real work-elimination rewrite, not a new probe.

## Resolver admission and safety

- New independent source injector `ApplyMinimapStealthWidgetLocality` and semantic catalog entry; current runtime threshold **3 ms/s**.
- Exactly one matching `ObserveAfter` callback of that name; source proof must match the complete PASS5 visibility logic after disregarding whitespace and standalone comments. Unknown author changes fail closed.
- Explicit marker `G-CET semantic:minimap-stealth-widget-locality` and source postconditions prevent duplicate application. Generator stages only `Minimap Widgets/init.lua` in the reviewable pass; the live mod is **read-only**.
- Older `minimap-widgets` timer-rule selector no longer claims this same callback, avoiding ambiguity and preserving onUpdate/Update admission where appropriate.
- `tests/MinimapStealthWidgetLocalityContract.ps1` verifies generation, exact source file set, marker, original unchanged, idempotence, spacing variation and wrong-source fail-close.
- The user's direct one-file opt-in ZIP is generated exclusively from the live `Minimap Widgets/init.lua` and can be installed through Vortex as a standard game-root file override. The original mod can be restored by disabling/removing this override.

## Required gameplay regression and measurement

With the same mod stack/save, test minimap on foot, car, combat, elevations, above/below arrow visibility, clamped mappins, NCPD/enemy indicators, changed map configuration and game save reload. Compare this callback's exclusive ms/s and scoped allocation operations/bytes against the fourth capture with roughly matched gameplay. Review rollback if any minimap visibility discrepancy occurs.

[Fourth capture evidence](../../gc/captures/FOURTH-G-GC-PDB-PRODUCER-ANALYSIS-2026-10-14.md) · [Resolver contract](../PROJECT-CONTRACT.md).
