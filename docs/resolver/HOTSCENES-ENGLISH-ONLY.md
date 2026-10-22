# Personal Hotscenes: fixed English UI and Lua worker capacity

**Status (2026-10-22):** explicit owner-requested feature cut
implemented in the Resolver. Do not distribute as the default Hotscenes
semantic optimization or silently apply to other installations.

## How opt-in works

In the Resolver application, enable **Personal pass: Hotscenes English-only
(remove localization)** before generating the optimization ZIP. The option
persists alongside the Resolver application in
`G-CET-Resolver.settings.json`, not in Cyberpunk's CET mod folder.

The equivalent command-line flag is `--hotscenes-english-only` with
`--generate-pass` (no settings file needed). By default, Hotscenes is
unchanged. The old test file `G-CET-UserOptions.json` in CET `mods`
is no longer read and should be removed if installed.

Only `hotscenes/init.lua` is considered; unknown source structures
fail closed without blocking other mods.

## Exactly what is removed

- Language JSON file scanner and catalog; game OnScreen language selection
  and remembered language restoration; language-refresh event work.
- Translation-file/template exports (including 4 text writes) and
  text replacement machinery used only for localization.
- An unreferenced `vectorDistance2D` local and `isFemaleScene` local.
- The variable `currentUiLanguage`, with the plugin performer key fixed
  to `"en-us"`.

Kept: `uiDefaultStrings` and English `nativeUI` default strings;
NativeUI initialization/update; ordinary settings and scene lifecycle;
save/restore behavior outside language preference; playback, dialogs,
observer ordering, addon behavior and runtime wake signals.

## Composition

This specialized stage precedes `GenericReadCoordinatorSource`.
The reduced main Lua needs only a **single local module import** to
adopt the 13 deferred generic native getter families; their worker
retains the existing 0-Engine provider/fallback semantics. This is a
designed capacity path, not an assertion of actual game performance.
No SHA-matching or version pinning is used.

Tests: `tests/HotscenesEnglishOnlySourceContract.ps1` checks explicit
opt-in, retained English behavior, rerun idempotence, unmodified live
files and refusal on source drift. In addition, the personal patch was
compared to the supplied `modsLIVEcopy.zip` source and parsed by Lua
5.4 after neutralizing the game's 16 `ULL` numeric literals in **both**
original and transformed source. That parser check is not a substitute
for an actual CET/LuaJIT game launch.

## Remaining proof

Replay the current installed stack with the new Resolver. Inspect the
exact Hotscenes output files, active local peak, and lack of stale
language functions. Execute a game regression covering NativeUI,
performer names, scene playback and session reload. Only then mark it
game-verified.
