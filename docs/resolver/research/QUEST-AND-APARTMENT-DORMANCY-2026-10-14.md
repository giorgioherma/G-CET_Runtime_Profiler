# Version-fluid optimization of dormant quests and Yaiba Penthouse — 2026-10-14

**Status:** Source-level optimizations **CI-verified and packaged** in [main #38605963886](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38605963886) (**all four jobs successful**), including original Minimap 3.1.7/pass5, two-file Ziggy v3→v4 upgrade, Tunnel original/live composition, and Yaiba original-source generation/idempotence/source drift. **In-game A/B and full quest/apartment gameplay validation remain pending.** Native DLL and both product versions (**v1.2.7**) unchanged.

### Post-release live-stack marker upgrade fix

The user's `G-CET_PASS_20261014-171003.json` confirmed that **Minimap + Yaiba generated**, but already G-CET v3-marked **Tunnel + Ziggy were incorrectly skipped** even though `AlreadySatisfied=false` and current-source proof passed. Root cause was a second legacy marker-only check in `SemanticInjectors.Apply`, not a missing source file or a version/hash mismatch. The two audited upgradeable rules now bypass that legacy check **only when already admitted by current source proof**, retaining the old v3 optimizations and appending v4 state handling. The actual uploaded `mods(5).zip` SHA256 hashes match all relevant sourceSha values. CI [#38605963886](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38605963886) **SUCCESS**, including the v3-marked source → Tunnel passive-status and Ziggy 1Hz stage upgrade, two-file output, nonduplicated wake function, and live-input read-only tests. No gameplay performance claim follows from CI.

## Evidence and originals

User provided and we inspected both the live `mods(5).zip` and unmodified source distributions:

- Minimap Widgets 3.1.7 original (6 Lua files, `Minimap Widgets/init.lua`) and live, previously optimized PASS5 Minimap Widgets source.
- Big Adam 4 original (`tunnel_rescue`, 16 Lua files) and the live v3 G-CET source.
- Starman: A Song in Pieces Dev17 original (`ziggy_last_play`, 6 Lua files) and live G-CET v3 source.
- Yaiba Penthouse 1.3.0 original (`yaibaPenthouse`, five Lua files) and current live stack.

Fourth native G-GC capture measured the following, **exclusive Lua callback** rates (not attributed share of owner-zero G-GC memory):

| Mod/hook | Total / duration | Exclusive callback ms/s | Maximum exclusive callback / spikes |
|---|---|---:|---|
| `tunnel_rescue` `onUpdate` | 519.833s / ~51.56 calls/s | 5.724675 | 175.9663 ms / 159 spike events |
| `ziggy_last_play` `onUpdate` | same | 4.099737 | 18.942 ms / 3 |
| `yaibaPenthouse` `onUpdate` | same | 3.660565 | 1.0554 ms / 0 |
| `Minimap Widgets` stealth elevation ObserveAfter | 73,989 calls | 5.679764 | 29.1114 ms / 1 |

Observed maxima can overlap collector pauses; **these numbers do not prove the specific rewrites save that total**. User confirmed neither optional quest was started during capture. Merely being registered as a CET `onUpdate` callback is not itself a bug; aim to eliminate avoidable *work* while inactive and preserve reliable activation.

## Changes, admission, wake behavior

1. **Original Minimap 3.1.7 support:** `minimap-stealth-widget-locality` now accepts **either** complete 3.1.7 original callback flow (including the unused `shouldShow` getter) **or** deployed PASS5 flow. Both match through source semantics after ignoring whitespace and standalone comments; standalone/double-quote registration variations are tested. The injector keeps author visibility branches and obtains above/below widget references only once per invocation. Unknown control-flow changes fail closed.
2. **Tunnel v3 additive upgrade:** pre-existing v3 0.15s/0.5s outside cadence and active gameplay code are *preserved*. New `G-CET semantic:tunnel-rescue-quiet-idle-status-v4` qualifies only at the final `status()` writer. When `H.phase=='outside'`, `H.session`, no arming, recovery, auto/manual action, error, or active visit: passive diagnostic `status.json` snapshots use **8s** rather than **2s**. As soon as a condition changes, it reverts to 2s. The existing `JournalOffer.update`, input, rescue, environmental effects, cleanup, native wake and gameplay controller cadence are untouched. A legitimate older/synthetic shape with no periodic status writer skips only this optional improvement; it must not be rejected because the optional status logger is absent.
3. **Starman v3→v4 additive upgrade:** the existing `__gcetZiggyOfferedFar` probe still checks every rendered frame and immediately rejects dormancy near Milo, during dialogue, while game/session is unavailable, if the player identity changes or the quest moves past offered. Only the **far, unaccepted, offered** controller changes its accumulated tick gate from **0.25s to 1.0s**. Existing timed navigation and recovery remain. Already-v3 current source is changed *in place*; original author archive gets existing v2 navigation + 1Hz far lane with no nested duplicate wrapper. Both navigation/init files receive the new completion marker so the two-file semantic is idempotent.
4. **Yaiba Penthouse original/current source:** `bathtub.update(player,pPos)` exits *before* unconditional `ui.update()` only when the player is more than **iconRange+1m** from bathtub, **both** the bathtub state and the *shared* interaction UI have no active hub, no pin, and no pending water application. The fast distance check uses plain Lua numeric position fields before allocating engine vectors. On approach, any hub/pin/pending state or session reset, normal author `ui.update`, mappins, water, fill prompt and visibility remain unchanged. Lamp/candle `gameObject::OnGameAttached` observers and update methods stay untouched. The original author 1.3.0 version and already-modified live `bathtub.lua` share the same proven structure.

## Version-fluid invariant

[HANDOFF CORE](../../HANDOFF/CORE.md) now explicitly requires original archive → live modified source → benign whitespace/comments/quote changes → idempotence and unknown-change fail-close to be tested. Do not use exact file hashes, version numbers, fixed offsets or the presence of prior G-CET markers as sufficient admission. A substantive changed quest/UI condition is *not* a formatting variation.

## User-provided reversible direct pass

The current-deployment, **live-source-exact** `G-CET_Pass_Quest_Idle_Tunnel_Starman_Yaiba.zip` contains only three amended Lua files under the standard Cyberpunk CET game-root layout and a hash manifest. It is not a substitute for a version-fluid Resolver pass, and should not be installed together with the same Resolver-generated transforms. Roll back by removing the Vortex override; do not overwrite or reset other installed passes.

## Regression plan

- Minimap: original 3.1.7, PASS5 live, quotes and comment/space variations, unknown visibility logic refusal.
- Tunnel: cold/offered, initialized inactive and far vs approach/armed, quest return/reunion, loading/paused/recovery; ensure no additional active-state tick throttling; status snapshots only relax when unquestionably idle.
- Starman: unaccepted/far, near 130m boundary, dialogue and quest pickup, session reload, return/listening, lost quest markers and timed navigation.
- Yaiba: fast travel away/back, interaction range and visibility, bath water on/off, native hub state after save reload, lamp/candle changes and associated attachment events.
- Runtime performance/GC A/B remains unverified until user installs a pass and captures comparable gameplay; avoid assigning GC owner-zero bytes to any of these callbacks.

[Fourth native capture](../../gc/captures/FOURTH-G-GC-PDB-PRODUCER-ANALYSIS-2026-10-14.md) · [Resolver contract](../PROJECT-CONTRACT.md).
