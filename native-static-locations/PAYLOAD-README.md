# G-NativeRuntime — independent RED4ext native service

This artifact contains the separately compiled `G-Engine.dll`.
It is **not** the CET profiler ASI and must never replace
`cyber_engine_tweaks.PROFILER.dll`.

Expected installed layout:

```text
red4ext/plugins/G-NativeRuntime/G-Engine.dll
```

## Bootstrap policy (native CI75 source)

- **No `G-Native.locations` file:** initialize a valid empty location index.
  Source-proven Lua owners may add coordinates later using
  `GCETStaticAddAnchor`; they must arm them and wait for a valid snapshot.
- **Valid GCAS manifest present:** load its source-proven static anchors.
  Dynamic registration can add additional independent anchors.
- **Invalid or unreadable manifest present:** refuse the native location
  coordinator. Never silently reinterpret corrupted data as an empty manifest.
- **No valid player position, unavailable service, or stale snapshot:**
  keep state UNKNOWN and execute original authored logic.

The Resolver integrates four **source-specific** owners (poker, tunnel
rescue, illegal mechanic, Dedka) through a single additive 0-Engine host.
The native service evaluates distances; it does not infer quest activation
or turn arbitrary gameplay callbacks off.

Normal generated Resolver optimization ZIPs bundle this native DLL
as a game-root overlay. The independent native CI artifact remains useful
for developers; it is not a required separate user installation. Neither
sample anchors nor the `G-Native.smoke-probe` marker belong in player ZIPs.

The native player-state API is independent of the manifest. The optional
40m smoke probe is only for deliberate developer testing. The native
core/Windows build tests have passed; the combined four-owner game
behavior remains to be validated in game.

Disable the old `G-CET-StaticLocations` overlay before activating the renamed runtime. The DLL refuses duplicate legacy ownership; it does not delete installed files. Existing GCET RTTI names and GCAS data format remain unchanged.
