# Native player adoption: installed source inventory (stage 1)

## Why no gameplay capture is required for discovery

G-CET already knows the game root when INSTALL runs. Source discovery
therefore runs **at installation**, writing
\`bin/x64/plugins/.cet_runtime_profiler/NativePlayerAdoptionCatalog.json\`.
Resolver also rescans the **current installed CET Lua sources** while
generating \`G-CET_Resolver.json\`, so a mod update does not require a capture
to refresh source understanding.

\`nativePlayerAdoptionCatalog\` is independent of measured callback rows:
it scans all installed non-infrastructure Lua files (bounded to 20,000 files,
2 MB/file). Captured, unmeasured and dormant callbacks are treated equally.
Each \`GetWorldPosition\`, \`GetWorldOrientation\` and \`IsInCombat\` callsite
includes its source line, receiver expression, player/class-origin tag and
the remaining native compatibility condition. Unknown identities stay
unknown. No unsupported claim of 100% dynamic invocation coverage is made.

## Why generic native Vector4 rewrites are not yet allowed

The independent RED4ext Native Player Snapshot exposes \`GCETPlayerX/Y/Z\`,
epoch and status, not a single CET-compatible \`Vector4\` with a proven
per-callsite frame-phase freshness guarantee. Blindly rewriting
\`player:GetWorldPosition()\` could create three Lua-to-C crossings,
reconstruct a different \`w\` component or return a stale position.
\`PlayerPuppet\` registration receiver identity alone does not establish
these value and phase contracts.

The catalog therefore explicitly sets \`NativeRewriteAuthorized=false\`
and \`TYPED_VECTOR4_AND_CALLSITE_FRAME_FRESHNESS_NOT_PROVEN\` for these
sites. It is **not** a hidden native substitution or version SHA lock.

The four pre-existing source-proven native **static-location** semantic
adapters (poker, tunnel, illegal mechanic, Dedka) remain eligible through
their existing source/lifecycle rules and generated pass stages. Their
near/far transition contract does **not** transfer to arbitrary player
getter calls.

## Completion path

A generic native position adoption requires an ABI/backend able to supply
CET type-compatible player position data at a compatible freshness epoch,
plus evidence that the rewrite has net native-boundary savings. Once
the actual contract exists, the inventory provides exact source-driven
candidate sites and the generator can admit only compatible operations
automatically. No fabricated runtime call or mandatory 30-second probing
is necessary for static eligibility. The first gameplay capture after an
admitted native rewrite verifies cost and behavior, not source discovery.

Native profiler binary and RED4ext plugin remain separately packaged.
Existing ASI restore hash checks are unchanged.
