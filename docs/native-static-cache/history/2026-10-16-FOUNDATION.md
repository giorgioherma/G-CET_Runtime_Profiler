# 2026-10-16 — Native shared-location foundation (historical milestone)

**Branch:** `dev/native-static-anchors-experimental`. Dated record; do not update it as the rolling current task.

The independent RED4ext service was extended from the original 128m spatial index + compiled GCAS v1 manifest to: 8192-event per-consumer cursored journal, explicit snapshot ACK/resync, owner arm/removal, 14 diagnostic metrics, one native Running-state player-position coordinator and session/invalid-player fail-open, one-time owner-provided runtime anchor registration with tombstones, a shared CET journal consumer, an optional G-RED scheduler consumer and a source-matched R4 Lua fallback compatibility wrapper.

Representative implementation sequence: native coordinator `8cc9083`, Linux namespace fix `986d1df`, SDK GameState type fix `505ea0a`, CET bridge `cadb90c`, G-RED adapter `ef7590c`, G-RED startup fix `c73f520`, dynamic anchors `887e042`, R4 bridge and follow-up tests and stack-fixture fix ending at `b29ecf3`, documentation commit `51ce694`. Early experiments had CI failures and were repaired; **[run #20](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38911428092)** at `51ce6942012686fd08534941da0657bb16fe4827` completed **SUCCESS** on Windows native DLL and Linux native/Python/Lua tests.

**What that does NOT prove:** actual game player RTTI getter, REDscript compilation, CET native export availability, game session/movement/fast travel safety, author wake equivalence, native-replaced FAR scanner retirement, or end-user FPS improvement. These remain pending independent test gates. A DLL plus empty manifest is only a *native smoke* deliverable.

Authoritative detailed contracts remain beside source: `native-static-locations/docs/STREAM-AND-METRICS.md`, `native-static-locations/adapters/README.md`, `docs/resolver/NATIVE-STATIC-WAKE-REPLACEMENT-CONTRACT.md`. Historical R4 source archives are fixtures, not evidence of installed game state. Profiler native #84 and production Resolver remain untouched.
