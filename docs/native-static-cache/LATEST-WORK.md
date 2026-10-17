# G-LookupCache — LATEST WORK (fluid)

**Current task:** Complete the first source-pinned, gameplay-equivalent CET native wake adoption for StreetGamesPoker, then extract its reusable proof pattern into generic Resolver admission. Branch `dev/native-static-anchors-experimental`. Do not touch production Profiler/Resolver CI, native Profiler CI #84, or the separate G-RED repository.

**Reviewed source:** The user-supplied `EXTENDED DORMANCY(7).zip` includes the precise source epoch already pinned in `reviewed_streetgames_pilot.py`: `StreetGamesPoker/init.lua` SHA-256 `e4594ed8eb7a1ad0a889068db0a5be258c498fab719a6f2ab13df408e6e9af84`, `StreetTables.lua` SHA-256 `a79edb7eca3632531d6a35d304857592a9f61f6f7c00337ecf9e0ff8346d0099`. All 40 source-roster sites and nine reviewed insertion points (including shutdown) were located. A local reproduction of the reviewed insertions parsed successfully with Lua 5.4 and reversed exactly to original text; that is **not** an in-game load or a production-generated gameplay validation.

**Implemented:** Shared native service, CET host, per-owner lifecycle, multi-anchor aggregate, original scheduler-job fallback, source-fingerprint-guarded reversible overlay, source-owned late adoption. New hardening in this continuation:
- Native consumer distinguishes successful original-job restoration from failed fallback, retrying only unsuccessful owners instead of latching them or repeatedly invoking already-restored owners; a failed new snapshot resets acknowledgements for every affected owner.
- Source gate retains a retryable `fallbackPending` state when a scheduler operation or author's restore callback fails.
- Source-pinned poker overlay now detaches native ownership on CET shutdown, in addition to rollback and session stage changes.
- Shared host must schedule successfully **before allocating native source IDs**, avoiding irreversible tombstone loss on temporary host startup failures.
- Added Lua regression tests for scheduler restoration, consumer fallback retries and host-not-ready source admission; Python regression for the poker shutdown injection.
- Experimental staged ZIP metadata and dependency marker now reside inside the game-root CET mod folder. Source CI checks archive path boundaries.

**CI verified:** Native [#39056164888](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/39056164888) at code SHA `95df3e02` passed Linux C++/Python/Lua and Windows RED4ext build. Source [#39056375951](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/39056375951) at code SHA `f736f578` passed staging and packaging tests. Subsequent documentation commits do not trigger path-filtered CI.

**NEXT:** Validate the exact generated full overlay/native RTTI and CET load in the user's installed game: original vs optimized, FAR→NEAR→FAR, already-near startup, save/load, teleport, session close, shutdown and native loss, chair spawn/despawn, stranded safety, active poker and other independent listeners. Measure authored FAR callback dispatch retirement, not just native service activity. A broad profiler capture is unnecessary until source pass equivalence is established.

**Hard bounds:** First pilot is source-pinned and opt-in, not generic automatic admission; runtime source integration is not yet game verified. G-RED compilation and unrelated AreaLoot stay separate. Source archive is an external validation fixture, not installed-stack proof.
