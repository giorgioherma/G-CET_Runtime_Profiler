# Native player state — frame-keyed, demand-driven handoff (experimental)

RTTI source: user-provided Cyberpunk 2077 2.31 dump (2026-10-12):
\`classes/ScriptGameInstance.json\`: \`GetFrameNumber() -> Uint64\` and
\`classes/entEntity.json\`: \`GetWorldPosition() -> Vector4\`.
These signatures do not alone prove that the frame counter increments
at the correct native/CET execution phase.

A pure-C++ \`DemandFrameCache\` is used by
\`GCETPlayerVector4()\`, which gets all four floats X/Y/Z/W through a
single typed RTTI call. The native provider reads \`GetFrameNumber\` from
\`ScriptGameInstance\`, returning the same-frame value on a hit.
- If the frame RTTI method is unavailable or invocation fails, **every call
  samples the game**; never reuse without an authoritative frame token.
- \`GCETPlayerVector4Live()\` bypasses frame reuse, always reading the
  engine for exact call-time semantics.
- \`GCETPlayerVector4Valid()\` reports validity of the immediately preceding
  position getter. The vector is a zero sentinel when invalid, never
  authoritatively substituted into mod source.
- Native RunningUpdate seeds the cache with its one sampled position
  when its frame token is available. Invalid samples clear the cache.
- Session enter/exit resets the cache.
- Counters \`GCETPlayerPositionCacheHits()\` and
  \`GCETPlayerPositionEngineReads()\` permit native measurement.
- \`GCETPlayerCapabilities\` adds bit 64, preserves bit 32 and API version 1.
- CET adapter \`TryReadVector4(requireLive)\` handles both modern and old
  native providers without per-mod hooks.

**Safety:** player snapshots identify the GameInstance player, not any
other instance of PlayerPuppet. A frame match alone does not establish
that an entity cannot teleport or change position inside that frame.
Resolver must only admit specific same-frame reuse where this is
semantically acceptable; for strict paths use the live getter. ABI
parity and measurement must be tested in game before broad automatic
replacements. Existing four source-proven native location adaptations
remain unchanged.

Native CI validates the independent C++ frame cache, fail-open behavior,
forced live reads, exact W retention and Lua capability negotiation.
