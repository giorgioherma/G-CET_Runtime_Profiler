# G-CET semantic reference patches

These diffs are development-only behavior guides for hot mods that were not present in the G-Mod Collection reference stack.

They are **not shipped with the resolver** and are **not version-specific replacement overrides**. Their purpose is to document the behavior we want the semantic injector to reconstruct against a user's current live source.

Admission rule: 3.0 ms/s only earns review. Production semantic admission additionally requires a compact, provable inactive/pending state and a complete safe wake path; bespoke perceptual/input timing does not belong in AUTO.

The semantic library stores:
- the measured runtime reason the mod is worth touching;
- the cadence/transition policy;
- broad source anchors;
- the generic injector family we want to derive.

The resolver must still graph the live mod directory, re-prove the current source shape, and fail closed when the source no longer satisfies the rule.


## Reviewed but deliberately outside automatic semantic optimization

- **OverclockedLynxPaws** — realtime movement/input system. Its grounded IDLE phase is still waiting for parkour/input transitions, so reducing that path to a chosen polling rate is bespoke tuning.
- **DualSense Support** — the author already exposes `handleUpdates`, but the current evidence does not prove the complete controller inactive/wake lifecycle. A hand-chosen 30 Hz output lane is therefore outside AUTO until that lifecycle itself can be proven.

Their former timing reference patches were removed so rejected AUTO ideas cannot be mistaken for production semantic recipes.

## Narrow retained rule

**NPCD_Hotline** remains only as an exact pending-work gate: empty task and SMS queues do not enter their workers. Interaction/UI cadence and the author's 0.5 s police/subscription core are explicitly outside the rule.
