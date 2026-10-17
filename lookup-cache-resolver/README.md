# G-LookupCache → Resolver evidence scanner

Experimental, **read-only** generic source discovery for the selected Cyberpunk game installation. This is not an installed mod, source patcher, or automatically admitted optimization.

Run from repository root:

```sh
python lookup-cache-resolver/tools/scan_stack.py --game "D:/Games/PC/Cyberpunk 2077" --output lookup-evidence.json
```

The scanner reads currently present CET mod Lua/JSON under bin/x64/plugins/cyber_engine_tweaks/mods and REDscript .reds/JSON under r6/scripts. It emits JSON with runtime/source-owner scope, source SHA-256 and owner fingerprint, line-numbered source excerpts for recurrence/player/distance/literal coordinates/remote obligations/mutation hints, and separable location-versus-independent-wake policy. Loose r6/scripts .reds files are units by file; nested files are grouped under their immediate directory. This is a filesystem heuristic, not true mod ownership, and the source review must establish exact scope and dependencies.

**Product goal, distinct from this scanner:** this is a **generic source-derived function/job transformation pass**, not per-mod semantic rules. It should recognize a source-proven static location as one wake trigger, prove exactly which location-only recurring work can stop while FAR, and retain independent phone, messages, PC/terminal, quest facts and lifecycle subscriptions. Mixed functions must be safely split; if they cannot be separated, do not suspend them wholesale. Remote events can update the native source-stage lease and wake author code even without movement. StreetGamesPoker is a reviewed fixture for one cancelable scheduler family, not a name-based production rule. The scanner does **not** implement this proof or transformation.

Every source candidate remains SOURCE_REVIEW_REQUIRED: no anchors are emitted, and safe_to_patch is false. A phone listener or quest event is **not a negative cache admission signal**; preserve it and evaluate whether the location discovery path can sleep independently. Name-independent source recognition is only a lead. Real source-equivalence proof, exact XY/XYZ geometry, stage transitions, false positives, source epochs and fallback must be handled by the Resolver.

**Critical native lifecycle:** FAR_DORMANT while waiting for proximity wake needs an **armed native anchor** or the mod can never wake. An INACTIVE location stage must **disarm** its native anchor, even if remote phone/quest listeners remain active. When the entire stack has zero armed anchors, the existing native Running hook avoids sampling player position. No extra per-mod timer, no permanently armed unused owners, no new location event activation authority.

Tests: `python -m unittest discover -s lookup-cache-resolver/tests -p 'test_*.py' -v`. Separate independent Linux evidence workflow: .github/workflows/lookup-cache-evidence.yml. Does not build the native DLL or production Resolver.

**Next step:** Pass these evidence records into the Resolver's source-analysis admission stage and generate first source-fingerprinted, idempotent, source-proven CET/REDscript rewrites with gameplay fallback. No automatic rewrite currently exists. [Full contract](../docs/native-static-cache/details/RESOLVER-EVIDENCE-AND-ARMING.md).


## First generic structural CET transformation family (experimental)

`tools/generic_cancelable_lane.py` now performs **name-independent source-derived admission**, not a known-mod lookup. It accepts a single source-local pair of 0-Engine `client.Schedule.Every` jobs when the FAR callback consists *entirely* of immutable literal XYZ subtraction, Euclidean squared-distance comparison against a constant squared radius, and `nearJob.Resume()/Pause()`. The near job's original callback is preserved, and all separate remote phone/quest/UI listeners remain untouched. This is intentionally one very narrow supported structure; the original callback is retained for fail-open and staged idempotently via the existing source engine.

A successful admission stages a game-root-only ZIP containing the revised original CET `init.lua`, mod-local wake binding and one shared `G-LookupCache` host. It does **not** bundle the RED4ext DLL. It requires unique, collision-checked positive owner/anchor IDs from the eventual Resolver allocator. Example developer invocation (source is relative to the selected game directory):

~~~sh
python lookup-cache-resolver/tools/generic_cancelable_lane.py --game "D:/Games/PC/Cyberpunk 2077" --source "bin/x64/plugins/cyber_engine_tweaks/mods/Example/init.lua" --owner-id 1400123 --anchor-id 1400124 --output generic-experimental.zip
~~~

**Refusal is expected** for Cron timers without pause/resume, unresolved helper/callee effects, mutable or multiple locations, jobs owned from other files or manipulated by independent events, mixed FAR callback side effects, changed/partial foreign source, noncanonical boundaries and unproved stage-based arming. This family never automatically disables a quest mod; it retires a source-proven location-only FAR lane and leaves the authored near callback and independent remote/event lanes intact. The current family assumes the location lane remains relevant for the lifetime of its job; quest stages that require anchor disarm/rearm need a later source-proven lifecycle transformation. Profiler workload evidence still determines which candidate to investigate first.

CI exercises name/coordinate portability, original/partial/optimized/restored source, tampering and ambiguous owner rejection, ZIP path safety, Lua 5.4 generated-source compilation and a mock runtime with independent remote quest events while FAR. **It has not run in Cyberpunk or proved FPS improvement.** The StreetGamesPoker reviewed pilot is a separate regression example, not an admission rule and not automatically admitted by this strict structural family.
