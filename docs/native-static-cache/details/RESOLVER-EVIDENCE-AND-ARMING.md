# G-LookupCache — generic Resolver evidence and lifecycle

## Scope

Use only effective mod files in one selected game root, with explicit distinction between CET Lua/JSON and REDscript .reds/JSON. This is per-stack discovery rather than a hardcoded master list. No assumptions that a directory name uniquely identifies a mod across CET and REDscript; correlate only with actual source/dependency evidence.

Executable first stage: lookup-cache-resolver/tools/scan_stack.py. It produces g-lookupcache-resolver-evidence-v1 records with owner/source fingerprints, SHA-256 per file, line excerpts and category counts. All results are leads, not proof; the report forces approved_transformations=0 and generated_anchors=0.

## The critical separable-wake decision

A mod may be **location-bound in one part** while still needing phone, quest, message, input, lifecycle and remote callbacks elsewhere. Those independent events must continue and do not invalidate the location cache. Source audit must identify exact callback body responsibilities. Choose REPLACE_PROXIMITY_WAKE for a source-proven location-only scanner, SPLIT_MIXED_WAKE when the function also performs independent work, GATE_DOWNSTREAM_ONLY when source registration cannot safely be retired, NO_GAIN if no work is saved, and REJECT for unproved or dynamically unsafe locations. Preserve quest activation: native NEAR only invokes the prior author-proven wake behavior, not a synthetic quest start.

## Native lease state

| Situation | Native anchor | Other author listeners |
| --- | --- | --- |
| Mod is FAR_DORMANT but approaching its fixed site is the next activation opportunity | **ARMED** to catch initial and enter wake | Independent phone/quest remains as required |
| Mod is NEAR/ACTIVE and still needs location enter/leave | ARMED | Author gameplay callbacks remain |
| Mod is active but a location is irrelevant during this stage | DISARMED for that source anchor | Author gameplay remains |
| Mod complete, disabled, uninstalled or a location-stage not eligible to wake | DISARMED/REMOVED | Independent remote listeners may remain |
| Native unavailable, source drift, invalid position or resync required | Fail-open, no inferred FAR | Original author behavior must resume |

The native coordinator is the sole player-position writer. With zero armed owner anchors, its core fast path stays dormant; a new owner can arm on source/session stage change and resume. **Never disarm every FAR owner that depends on native entry events**: that creates an unreachable wake. Equally, don't keep all installed locations permanently armed after quests complete. No blanket per-mod scheduled polling to check when to re-arm; lifecycle actions should drive changes. Changes in owner coordinates require a new unique anchor ID and tombstone the old one.

## Proposed phased Resolver integration

1. Scan the **selected installation** and emit deterministic source-evidence JSON with source and owner fingerprints.
2. Associate each probable location with the exact source-authored coordinate producer, stage epoch, original distance metric/radius and scheduler/hook registration. Mere regex counts are insufficient.
3. Enumerate all callback side effects and independent wakes, including phone and quest paths that must run even if the player is far. A mod with one fixed source point remains eligible. Exclude dynamic moving entities unless a separately proven bounded source epoch is available.
4. Admit only source-matched source variants. Generate registered native anchors and an owner-specific arming/disarming adapter; native journal snapshot before active delivery; UNKNOWN and missed-events restore original author discovery. No second per-mod onUpdate/Cron checking native State.
5. **Remove old recurring discovery work** in normal healthy native mode, or split the mixed callback. Do not remove independent phone/quest listeners; leave original exact interaction checks and active-gameplay timers.
6. Compose with the existing generic and semantic Resolver transforms from pristine/live/previously optimized source; second pass must be idempotent, drift must fail closed; output is a reversible game-root-only overlay, no direct live mutation.
7. Validate initial NEAR, FAR→NEAR, leave/re-enter, session state, fast travel, remote phone/quest wake, source drift, failure and reactivation, then quantify work retired versus the native shared worker tax. No claim of savings on registration-only results.

## Current implementation boundary

The generic scanner remains read-only triage. In addition, an **explicitly reviewed, source-fingerprinted CET pilot** now stages an opt-in StreetGamesPoker scheduler rewrite with a complete 40-site roster, one owner/native consumer, original far-job retirement on healthy native snapshots, native FAR housekeeping, independently retained gameplay lanes, and a reversible Lua overlay. It is *not* generic automatic source admission, has not been game loaded and has no measured runtime savings. The REDscript adapter remains uncompiled in the user's modded game.

### Runtime failure and teardown obligations

- A failed author fallback is **not** a completed fail-open transition. Native consumer and source job gate must keep retrying original `Resume()`/source restoration until successful; do not permanently latch a failed callback just because `pcall` completed.
- Shared 0-Engine scheduler admission must succeed before native source IDs are allocated; otherwise a temporarily unavailable scheduler permanently tombstones a valid source epoch and later retries cannot attach.
- CET shutdown and failed source construction must detach the owner, retire/disarm native anchors and relinquish the shared subscription without stopping other owners. Fully inactive stage cleanup remains separate.
- The normal far-scanner `StreetTables.update` also performs chair despawn and stranded safety. Only proof-guided housekeeping on native FAR/teleport with retained active gameplay is eligible for retirement.
- Source syntax and exact fingerprint checking are necessary but not sufficient. Require in-game RTTI, first snapshot, first-near, leave/re-enter, save/load, remote/active gameplay and original-job dispatch comparison before accepting the optimization.

This contract is not a claim that arbitrary installed mods can safely be transformed.

References: [native wake replacement contract](../../resolver/NATIVE-STATIC-WAKE-REPLACEMENT-CONTRACT.md), [cache core](../HANDOFF/CORE.md), [scanner](../../../lookup-cache-resolver/README.md).
