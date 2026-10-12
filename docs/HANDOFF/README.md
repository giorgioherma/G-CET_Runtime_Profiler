# G-CET — permanent project handoff

**Start here in every new development chat.** Read the documents below; then verify current GitHub branch HEAD, CI status, uploaded captures and live/original mod sources. Earlier assistants' statements are *not* substitutes for source or test results.

## Reading order

1. [PROJECT-CONTRACT.md](PROJECT-CONTRACT.md) — architecture, two optimization families, risk/payback, version tolerance, composition, and developer rules.
2. [GC-RESEARCH.md](GC-RESEARCH.md) — **primary current research frontier**: reduce needless Lua allocations and redundant work *at the source*, using G-GC evidence without inventing causality.
3. [ACTIVE-STATE.md](ACTIVE-STATE.md) — branch/version/native pin, recent Poker/Tunnel/Ziggy semantic work, remaining tests and priority work.
4. [NEXT-CHAT.md](NEXT-CHAT.md) — short reusable continuation prompt.

Authoritative technical references: [Architecture](../ARCHITECTURE.md), [0-Engine workload services](../0-ENGINE-WORKLOAD-SERVICES.md), [G-GC Tracer](../G-GC-TRACER.md), [GC phase diagnostics](../GC-PHASE-ALLOCATION-DIAGNOSTICS.md), and [unified evidence plan](../MEGAMIND-INTEGRATION-PLAN.md). The active rules live in [semantic-library.json](../../resolver-knowledge/semantic-library.json).

## Project in one paragraph

We take **original/current Cyberpunk 2077 CET mod source**, profile real gameplay, and have **one Resolver** identify existing optimizations and apply *safe generic changes first* and *source-proven semantic changes second*. Both may apply to one mod; the Resolver recognizes generic, semantic and partial 0-Engine integration and adds only missing safe work. The generated source should match **behavioral code patterns across compatible mod versions**, not version tags, exact source hashes, incidental formatting or callback registration IDs. The Resolver emits a ZIP based on user-present files; it does **not** silently patch the live game. User installs and tests output.

## Principles that cannot be lost

- **Generic is broad, conservative, high leverage.** We actively seek new generic rules: callback consolidation, finite action routing, safe shared-provider/state reuse, cheap no-escape allocation removal, and other mechanically proven improvements. Small wins are worthwhile when risk is almost zero and gains aggregate across many mods.
- **Semantic targets significant offenders.** Dormancy, event wake, near/far state, quest progression, unique polling and mod-specific cadence are more invasive. Require substantial measured payback, complete gameplay/lifecycle proof and source tolerance across versions.
- **GC is the biggest remaining optimization question.** The user wants to stop unnecessary high-volume data flowing into LuaJIT garbage collection, not just tune the collector. Search for duplicate large temporary tables, high-frequency full queries, repeated snapshots, shared read-only data, and occasional huge allocations.
- **0-Engine is infrastructure**, not an automatic rewrite: Scheduler plus opt-in WorkQueue, PhasePlanner, FrameListeners and StateSignals.
- **No third optimizer.** GC and burst data augment the current Resolver; neither grants automatic source rewrite authority.
- **Never trade away author features.** Quest objectives, shard discovery, dialogue, map categories/pins, UI, input, timers, session reset, cleanup and ordering are requirements.
- **Fix the generator**, not a generated broken mod as the product deliverable. Acceptance is **original mod → Resolver → complete optimized mod**.
- **CI ≠ gameplay verification.** The user observed >50% CET workload reduction on many real tested stacks, but not every feature of every mod was exercised. No universal claims.

## Session reporting standard

Identify exact branch HEAD/build/native version and capture; measured evidence vs inference; source files and already-present optimizations; why the proposed rule passes risk/payback; precise generated changes and tests; actual CI result; what still needs live game validation.

Keep this folder updated with major design or capture developments so future assistants do not reopen settled discussions or accidentally reverse working behavior.
