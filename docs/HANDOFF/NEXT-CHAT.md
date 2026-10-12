# Reusable continuation prompt for the next chat

Copy the following into a new project chat, with any new profiler captures and original mod archives.

> Continue development of `giorgioherma/G-CET_Runtime_Profiler`, branch `dev/resolver-profiler-handoff`.
>
> **Read these first from the latest GitHub HEAD:**
> `docs/HANDOFF/README.md`,
> `docs/HANDOFF/PROJECT-CONTRACT.md`,
> `docs/HANDOFF/ACTIVE-STATE.md`,
> `docs/HANDOFF/GC-RESEARCH.md`.
> Also inspect the current architecture, semantic catalog, actual original/live mod source, the capture in question, recent commits and Actions CI before proposing changes.
>
> **Core architecture:** One Resolver profiles real original CET mods, detects prior generic/semantic/0-Engine optimization, generates safe generic changes first and substantial source-proven semantic changes second. It must compose when both affect one mod, recognize incomplete integrations, and output only changed user-present mod files in a ZIP. Semantic identity comes from compatible behavioral/source patterns, not exact mod versions. Always preserve gameplay, state transitions, UI, quest/dialogue, pins, error/fallback and idempotence.
>
> **Risk/payback:** low/near-zero-risk broadly useful generic rules justify small aggregate wins. Timing/dormancy/source-specific semantic changes require material measured offenders and complete lifecycle/rapid-wake proof. Treat bursts and frame pacing separately from average CPU. Do not optimize because a heuristic score looks high.
>
> **Current primary objective:** G-GC tracing and source-level analysis of excessive allocations. Seek new broadly safe generic rules to avoid repeated queries, ephemeral lists/tables and redundant data production; investigate cross-mod immutable shared heavy lists with complete freshness/invalidation proof; classify rare huge burst producers. Semantic remains appropriate for major dormant/offered/far quest systems. GC evidence does not authorize an unproven source change or prove which mod caused a collector pause.
>
> **Known examples:** Poker two-file PASS4/PASS4C from original source, including aggressive 25m proximity and preserved map pins; Big Adam/Tunnel generating-source callback placeholder fix; Ziggy/Starman combined v3 navigation+130m/250ms offered-far gate. Current managed package is 1.2.3, **native product remains SHA-locked 2.15.0**, experimental 2.16 is not release-safe. Confirm latest CI/build and in-game validation, never assume them.
>
> **For the current task:** analyze supplied source/capture, distinguish measurements vs hypotheses, inspect existing patterns and rules, choose source-proven generic or semantic opportunity, implement through the existing Resolver, add original-source + compatible-version + composition + idempotence tests, run CI, and clearly report exactly what was generated and what still requires gameplay testing.
>
> **Do not** ship ad hoc local mod patches as a substitute for a generating-rule fix, add a third optimizer, rewrite 0-Engine indiscriminately, or promote native tracing because managed build succeeded.

## Session startup checklist

- [ ] Check branch HEAD and latest CI. Read all four handoff docs and corresponding current code.
- [ ] Read real capture plus untouched original source and currently deployed changes; verify source graph/owners.
- [ ] Split CPU, bursts, alloc bytes, ops, births, native GC phase and unattributed evidence correctly.
- [ ] Determine which generic/semantic/0-Engine changes are already satisfied or partially implemented.
- [ ] Evaluate risk/payback at full affected system level; verify every gameplay wake/invalidation.
- [ ] Generate an actual Resolver pass from original mod and run regression/variant/idempotence checks.
- [ ] Report CI, observed/estimated savings and remaining in-game requirements separately.
- [ ] Update `docs/HANDOFF/ACTIVE-STATE.md` when the task materially changes the project.
