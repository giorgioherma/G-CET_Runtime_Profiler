# Typed native Player Vector4 handoff

The independent RED4ext plugin already samples the player's real
`RED4ext::Vector4` through RTTI. This update retains X/Y/Z/W and exports
`GCETPlayerVector4() -> Vector4` in **one** typed RTTI boundary crossing,
rather than making three scalar getter calls and reconstructing it in Lua.

## Compatibility

- Existing scalar and static-location APIs remain unchanged.
- Native `GCETPlayerCapabilities` adds bit 32; `GCETPlayerApiVersion` remains 1.
- `GCETPlayerStatus()==2` indicates a currently valid stored sample.
- `GCETPlayerEpoch()` tracks session changes and
  `GCETPlayerRevision()` tracks value changes including W.
- No subscriptions: no player RTTI sampling by this service.
- If the sample is absent, the typed getter returns a zero-initialized
  sentinel Vector4. **Never substitute that sentinel for an authored getter.**

The optional CET adapter `TryReadVector4()` checks status and returns
the typed native value untouched; older native plugins without bit 32
continue using the original scalar interface.

The native Running-state update phase may precede or follow a CET
observer's update phase. Status and epoch are **not** sufficient to prove
same-callback freshness. Generic Resolver replacement of
`self:GetWorldPosition()` must remain disabled until the sampling phase
and fallback are verified. The existing source-proven near/far semantic
adapters are independent.

Native tests validate full W preservation, W-only revisions, invalid
samples and lifecycle invalidation. CI compiles the RED4ext plugin and
checks that the typed function name exists in the compiled DLL.
A gameplay A/B test is still necessary to verify CET Vector4 marshalling
and world-position equality with the original getter.
