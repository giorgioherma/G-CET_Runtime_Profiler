# G-CET semantic reference patches

These diffs are development-only behavior guides for hot mods that were not present in the G-Mod Collection reference stack.

They are **not shipped with the resolver** and are **not version-specific replacement overrides**. Their purpose is to document the behavior we want the semantic injector to reconstruct against a user's current live source.

Admission rule: a mod only enters this corpus when profiler data shows a material callback at or above the current 3.0 ms/s threshold.

The semantic library stores:
- the measured runtime reason the mod is worth touching;
- the cadence/transition policy;
- broad source anchors;
- the generic injector family we want to derive.

The resolver must still graph the live mod directory, re-prove the current source shape, and fail closed when the source no longer satisfies the rule.
