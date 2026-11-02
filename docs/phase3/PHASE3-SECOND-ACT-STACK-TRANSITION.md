# Phase 3 — Second Act: Mod stack transition

**Decision date:** 2026-11-02  
**Status:** User-directed stack change; replacement stack and first capture pending.

## Scope of the transition

The user is ending optimization work on the current gameplay mod selection and will remove some mods from the live installation at their discretion. The existing optimization implementations are **left as they are**; this is not an instruction to delete, disable, revert or rewrite their generators, workers, tests or supported installation paths.

The next Phase 3 work will use a **different installed CET mod stack** selected by the user. The exact new inventory, removals and additions are **not yet supplied or verified**. Do not infer a fixed list from this decision or label any specific mod as removed.

## Evidence boundary

Prior Phase 3 captures, source analyses, gameplay findings and controlled benchmark results remain valid **for the previous stack and recorded conditions**. Keep them as historical evidence; do not use their mod pressure rankings or their absolute rates as if they described the new installation.

The user will provide a fresh capture of the new stack. Its initial purpose is **exploratory prioritization**, not a mandatory exact-route, exact-duration A/B against the old stack. The established COMBAT-POLICE-120 protocol and its historical measurements are preserved unchanged for cases that actually require controlled comparisons. Across changed mod inventories, describe observations as stack-specific rather than claiming a single optimization caused differences.

## Resume point

When the user supplies the new capture and/or mod inventory:

1. Identify which mods are actually present using current evidence; do not assume former stack membership.
2. Profile the new stack on its own terms, ranking real work and allocation/GC pressure before selecting targets.
3. Continue development only after identifying user-approved targets, with existing authoritative generators, dynamic source discovery, one-pass generation, staged compatibility, and CI/gameplay validation rules unchanged.

**No optimization code or instrumentation changes are authorized by this transition note alone.**
