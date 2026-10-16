# Experimental CET native-static-location consumer

This module is an **opt-in adapter** for the *existing* shared CET host (e.g. a 0-Engine runtime dispatcher), not another CET mod and not another `registerForEvent("onUpdate")` subscription.

A source-proven Resolver transformation creates one Hub with a unique positive consumer ID and binds each admitted owner to `onNear(anchorId,isSnapshot)`, optional `onFar(anchorId,isSnapshot)`, and **mandatory source-original fail-open fallback** `onFallback(reason)`. The host calls `hub:step()` on its existing update/scheduled event. It does **not** arm anchors, take over quest logic, or automatically invoke author interactions. Owner source adaptation must control `GCETStaticArm(id,armed)` at the correct stage.

The required `native` table exposes callable `Game.GCETStatic*` RTTI wrappers. Pass the Game proxy only after CET initializes; these native exports must be validated in-game, including ABI visibility, before shipping. The adapter never invokes `GCETStaticUpdatePlayer`: the RED4ext running-state coordinator exclusively owns that.

Lifecycle: subscribe -> initial owner-scoped snapshot -> ACK -> bounded independent event poll. On missing native coordinator, unknown anchor, overrun, native error, or callback exception the module executes the **original source-proven fallback**. A callback error tears down subscription so the next successful step re-snapshots. This bridge alone does not authorize removal of author polling or guarantee first-interaction timing; the Resolver must prove the fallback exists and current source semantics survive.

Development-only mock protocol test: `lua5.4 native-static-locations/tests/CETStaticLocationConsumerTests.lua`.

## Experimental G-RED consumer

`GRedStaticLocationHub.reds` is a separate optional overlay for installations
with G-REDruntime. It consumes **one all-owner native event stream** through
**one existing G-RED scheduler job**, registered only when an author adapter
calls `GCETStaticLocationHub.Get(game).RegisterOwner(owner, listener)`.
Source adapters subclass `GCETStaticLocationListener` to receive near/far
transitions and implement **mandatory original-behavior fallback** for unknown
service/position. It does not arm quest-stage anchors automatically.

The REDscript file has **not been compiled with the player's full REDscript
stack or game-tested**. It is not included in the native DLL artifact, and
no source wake may be retired on its authority. Validate compilation, first
snapshot, owner removal, load/fast travel, fallback and G-RED scheduler behavior
before admitting it to generated passes. It does not modify the G-REDruntime
repository or its production framework.

## R4 source-matched native cache backend (not production)

`LegacyR4NativeCacheBridge.lua` wraps the **existing experimentally optimized R4 `GCETStaticAnchors` module** and preserves `Register`, `Invalidate`, `Update`, `ResetSession`, `GetAnchor`, `QueryNearby`, `GetInfo` and owner handles. One-time author-provided anchor declarations become `GCETStaticAddAnchor` + `GCETStaticArm` calls; subsequent update notifications use the single native subscription. Failed/native-unavailable states fall back to the previously source-matched R4 Lua location worker. No extra author onUpdate hook is registered. An owner geometry reload allocates new IDs and removes the old anchors, preserving tombstone and session behavior. Test with `lua5.4 native-static-locations/tests/LegacyR4NativeCacheBridgeTests.lua`.

This wrapper is **not** an independent source transformer, and it must not be blindly deployed on a pristine mod folder. It only targets the earlier R4 source fixture that already has source-specific author callbacks and old wake removal, and its installed source fingerprint must match. The game RTTI method names and runtime behavior require explicit testing. Even with a valid native transition, an author mod's independent quest/active work remains untouched. Avoid using `QueryNearby` from hot callbacks; it intentionally performs on-demand enumeration.
