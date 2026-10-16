# Immersive Third Person upstream compatibility — 2026-10-16

Source reviewed: user-uploaded `immersive_third_person.zip`. The updated package contains `init.lua` (567,892 bytes; SHA-256 `2a47855f977de1168b67fa9dec47bf96f2881c722963260d3f4a3750d61b67e5`), `api.lua` and `camera_motion.lua`. Do not redistribute the upstream files in tests.

## Semantic behavior

- `immersive-third-person`: older original code still uses its established adaptive supervisor. The newer author onUpdate loop keeps API, movement, scene, camera, input and TPP maintenance on the original frame cadence. Six locally source-proven pending-state functions are gated only when their corresponding pending states are absent. The original function calls and delta are preserved whenever work is pending.
- `immersive-third-person-action-routing`: newer source has exactly 18 literal handled action names. Its original `handlePlayerAction` performs movement capture when third-person is disabled if game-camera takeover or move360 is enabled. Therefore the new route shares the already-installed 0-Engine native observer but **does not pause its subscriber when TPP is disabled**. The original protected callback and native Observe fallback remain.
- Existing v1/v2 optimized installations retain their earlier, separately verified semantics. New source markers are `__gcetItppUpdatedPendingGatesV3`, `__gcetItppRoutedOnActionV3` and `__gcetItppAlwaysListeningV3`.

## Tests and limitations

Dedicated Windows source-shaped CI #3 and #4 passed: current two-rule one-pass application, preserved original game-camera/move360 processing, unchanged API/motion modules, idempotence, unknown action rejection, and previously optimized older source. The exact user upload was also audited for its required source anchors locally.

The tests are not real gameplay verification. These changes do not claim FPS or native allocation savings, and do not rewrite Lua into native C. The Resolver only generates an overlay; it does not modify the live mod. The user will discard the previous pass overlay files, then run a fresh pass against the installed updated upstream mod.
