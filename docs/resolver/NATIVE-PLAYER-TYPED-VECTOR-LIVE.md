# Automatic typed native player-position handoff — source-first gated V1

The current native RED4ext service supports these version-independent
capabilities (negotiated at Lua runtime, **not from a per-mod hash**):

- \`GCETPlayerVector4()\`: frame-keyed native snapshot; may reuse another
  call's same-frame state; not yet eligible for general authored-source rewrites
  because same-frame teleports and other phase changes can matter.
- \`GCETPlayerVector4Live()\`: forced on-demand native engine read; typed Vector4
  preserving W; does not reuse a potentially stale update-phase snapshot.
- \`GCETPlayerVector4Valid()\`: validity of the immediately preceding native
  vector getter. On failure a generated adapter retains its exact original
  \`Game.GetPlayer():GetWorldPosition()\` expression.
- Capability bit 64 indicates this API family. Older plugin installations
  get original source behavior without requiring per-mod manual configuration.

The Resolver scans every installed Lua source even when no callback executes.
Only a lexically unshadowed direct \`Game.GetPlayer():GetWorldPosition()\`
expression is automatically eligible for the first guarded live recipe.
Other receiver aliases, \`self\` instance contexts, NPC references and
all other RTTI signatures remain *observations* until identity, side effects,
return type and execution phase are independently verified.

The generator attaches one identical local namespaced helper to eligible
source files, preserving comment/string masking, BOM/CRLF and the existing
200-local budget, and composes this before source-first shared getter reuse.
No gameplay callbacks are registered by this helper; native capability is
resolved once per mod initialization; runtime calls fail open to the original
getter. Source fingerprint in the overlay manifest remains tied to the
installed file; the source inventory is recalculated at generation time.
This is not a version-pinned per-mod rule.

The strict-live adapter is expected to provide *correctness*, not guaranteed
performance gains. Every native invocation and validity check has an RTTI
crossing. Profitability requires A/B measurement before enabling wider
cached substitutions. Manifest fields distinguish strict live substitution
from the unapproved same-frame cache rewrites.

RTTI future catalog: \`entEntity.GetWorldOrientation() -> Quaternion\`,
\`entEntity.GetWorldForward() -> Vector4\`,
\`gamePuppet.GetVelocity() -> Vector4\`,
\`PlayerPuppet.IsInCombat() -> Bool\`, and
\`PlayerPuppet.GetMountedVehicle() -> handle:vehicleBaseObject\`.
These must never be treated as automatically player-owned based solely on
observer class context.

Validation needed in gameplay: verify CET typed Vector4 marshalling,
W parity, validity/fallback semantics, old API absence, runtime mod updates,
and native/RTTI crossings cost. If the native side is not actually present,
source fallback must work. The existing four native static-location owner
semantics remain independent.
