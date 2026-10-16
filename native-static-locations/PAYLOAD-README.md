# G-LookupCache — experimental native payload

**Do not install as an active location optimizer yet.** This artifact proves the isolated RED4ext native plugin build. It does not include a source-proven per-installation `G-CET.locations` dataset, REDscript declaration, CET coordination adapter, source-approved author-wake replacement or Resolver-generated subscriber suspension.

Payload layout (once ready for integration): `red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll`. The compiled `G-CET.locations` file must be placed BESIDE it by the Resolver **only after** the Resolver identifies source-proven fixed-location mods installed in that specific CET/RED stack. No global mod list is forced on the user.

The native service fails safely with unknown state (`-1`) if its location data is missing, invalid or no valid player snapshot has been supplied. Do not use `State()==0` as a reason to suspend a callback until the source-specific coordinator, wake and remote-activation paths are verified.

This experiment does NOT replace `cyber_engine_tweaks.PROFILER.dll`, RED4ext core, 0-Engine, or G-RED. The two original Profiler and Resolver packages remain pinned and untouched.

## Diagnostic revision (opt-in only)

The native service includes a RED4ext Running-state **player-position coordinator** and bounded health logging. The source ABI is still not proven in-game even though the plugin DLL previously loaded successfully. The usual isolated native CI DLL package has no probe marker and makes **no performance improvement**.

For one deliberate diagnostic session only, a developer smoke overlay may add a file `G-CET.smoke-probe` beside the DLL and an empty GCAS manifest. When the game supplies a valid position the plugin creates a **40m passive native anchor** and logs first NEAR, FAR on leaving, and NEAR on returning, plus player-getter errors if relevant. No author mod callbacks are changed or invoked. Remove the independent smoke overlay afterwards.
