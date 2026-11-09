# Security and Binary Provenance

This document is intended for users, antivirus/security reviewers, and platform moderators reviewing the G-CET Runtime Profiler portable Windows package. **On the `cyberpunk-2.5-cet-1.38.1` branch, the current public release is v1.0.3 for Cyberpunk 2077 2.5 / CET 1.38.1.** The earlier v1.0.2 release targets Cyberpunk 2.31 / CET 1.37.1.

## What the application does

G-CET Runtime Profiler is a Cyberpunk 2077 development/profiling utility. Its normal operation includes actions that can resemble generic installer or mod-manager behavior to heuristic scanners:

- reads the selected Cyberpunk 2077 / Cyber Engine Tweaks installation;
- backs up specific pre-existing files before managed replacement;
- temporarily deploys the profiler-specific CET ASI;
- may deploy or adapt specific Lua integration files for supported 0-Engine layouts;
- records managed state under `bin\x64\plugins\.cet_runtime_profiler\`;
- collects profiler-owned output into the package-local `RESULTS\` directory;
- restores the original managed game files on request;
- starts the internal managed application through the small native root launcher.

These operations are part of the documented profiler lifecycle. The architecture and recovery rules are described in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Source-visible components

| Shipped component | Provenance |
| --- | --- |
| `G-CET-Runtime-Profiler.exe` | Built from `src/G.CETProfiler.Launcher/launcher.c` by GitHub Actions |
| `app\G-CET-Runtime-Profiler.App.exe` | Built from `src/G.CETProfiler.App/` using .NET 8 |
| `app\G.CETProfiler.Core.dll` | Built from `src/G.CETProfiler.Core/` using .NET 8 |
| Lua integration payloads | Stored as source in `payload/` |
| `payload\cyber_engine_tweaks.PROFILER.dll` | Custom CET 1.38.1 native profiler; ported build source is in `native-profiler-build/v2.11.0/` and the **release-staged** `MANIFEST.json` locks the shipped binary hash |

The complete manager/launcher build procedure is documented in [BUILDING.md](BUILDING.md).

## Package filename vs. installed filename

The native profiler is a DLL-format PE. To make that binary identity explicit to static scanners, the portable package stores it as:

`payload\cyber_engine_tweaks.PROFILER.dll`

During an explicit profiler installation, G-CET copies those **same hash-verified bytes** to the filename required by Cyber Engine Tweaks:

`bin\x64\plugins\cyber_engine_tweaks.asi`

This is a filename change only; the native profiler binary itself is unchanged.

## Native profiler ASI provenance — v1.0.3 / Cyberpunk 2.5

The **published v1.0.3 ZIP**, not the branch's historical tracked payload, is authoritative for the actual installed profiler binary:

```text
Shipped file:            G-CET-Runtime-Profiler/payload/cyber_engine_tweaks.PROFILER.dll
Installed CET filename:  bin/x64/plugins/cyber_engine_tweaks.asi
Target game:             Cyberpunk 2077 2.5
Target CET:              1.38.1
Native ASI SHA-256:      f06d00a684f57c93bcf90c9f8fbeb1c4ba56a9ada25904d98cee6a55621b370f
Upstream CET commit:     213151fa22c79182922a08a44e4a44d44739d1cd
Xmake:                   3.1.1
Windows SDK:             10.0.26100.0
MSVC:                    Windows CI-selected toolset (see build log)
```

The native builder clones the pinned official CET 1.38.1 source, initializes submodules, applies the published profiler source changes, and builds a new native profiler ASI. The source directory retains its historical `v2.11.0` name and some filenames retain `v1.37.1` labels; these labels are **not** the shipped v1.0.3 CET target. See the [CET 1.38.1 port notes](docs/CET-1.38.1-PUBLIC-PORT.md) and [native builder source](native-profiler-build/v2.11.0/).

The successful [native-only build #42437606920](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/42437606920) used source pin `8b7f743c3aa9874cc16c58b2efa4ce950f5b6a1f` and produced the native DLL identified by the SHA-256 above. The 2.5 package workflow retrieves those exact bytes, verifies their checksum and the pinned generating source, and does **not** rebuild CET for ordinary manager/Lua/documentation changes.

**Important checked-in vs. release-staged distinction:** the checked-in `MANIFEST.json` and `payload/cyber_engine_tweaks.PROFILER.dll` on this branch still contain older CET 1.37.1 baseline values. During the [2.5 package workflow](.github/workflows/cet-2.5-profiler-package.yml), CI replaces the staged DLL with the validated 1.38.1 native artifact and updates the staged `MANIFEST.json` to:

```text
targetCET.version:        1.38.1
targetCET.profilerSha256: f06d00a684f57c93bcf90c9f8fbeb1c4ba56a9ada25904d98cee6a55621b370f
targetCET.officialSha256: f0acdad154bd0033583e0f6e675ea18b4bdb60cda28133d792a4674e6d7f228a
```

The official reference hash identifies the **specific CET 1.38.1 ASI used in testing**, not every third-party CET distribution. The earlier 1.37.1 build is for [v1.0.2](https://github.com/giorgioherma/G-CET_Runtime_Profiler/releases/tag/v1.0.2); do not interchange its native binary with v1.0.3.

## Package controls

The [2.5 packaging workflow](.github/workflows/cet-2.5-profiler-package.yml) and [v1.0.3 publisher](.github/workflows/publish-cet-2.5-v1.0.3.yml) perform checks intended to make the public package inspectable and deterministic:

- downloads and verifies the pinned CET 1.38.1 native ASI rather than rebuilding unchanged source;
- verifies the release-staged manifest and integration payload hashes before building;
- builds the public manager from repository source;
- builds the native launcher from repository source;
- uses a normal ZIP rather than a self-extracting package;
- keeps `PublishSingleFile=false` so managed/runtime components remain visible;
- rejects public `.ps1`, `.vbs`, and `.cmd` manager scripts;
- removes public PDB files;
- verifies expected package paths before release staging;
- excludes validation fixtures and obsolete documentation from the final v1.0.3 ZIP;
- publishes a SHA-256 checksum as a **separate** GitHub release asset.

## File safety

The lifecycle core follows a preserve-before-mutate rule.

Before replacing a supported user-owned file, G-CET stores and verifies the recoverable original state. Restore validates managed state before replacing live files. Unexpected changes cause normal restore to stop rather than overwrite an unknown state.

Collected CET profiler output is copied and hash-verified before profiler-owned live output is removed. Unrelated files are not intentionally swept.

External frame-time captures are copy-only; the source capture owned by the external profiler is not moved or deleted.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the detailed transaction and recovery model.

## v1.0.3 canonical release identity

[Canonical v1.0.3 GitHub release](https://github.com/giorgioherma/G-CET_Runtime_Profiler/releases/tag/v1.0.3):

```text
ZIP:     G-CET-Runtime-Profiler-v1.0.3-CP2077-2.5-CET-1.38.1.zip
SHA-256: 6d729fb85c389cb310967c6805a44ebd7517394aeefd616fb7de75278ecda86c
```

The matching `.zip.sha256.txt` is a separate release asset, **not** included in the archive. Compare the full ZIP hash to the Nexus or antivirus scan; the native DLL hash is different. The v1.0.2 / CET 1.37.1 release remains separately available.

## Notes for Nexus Mods / security reviewers

For a compiled-code review of **v1.0.3 / CET 1.38.1**, inspect:

1. Manager and launcher source under `src/`.
2. [Native build source](native-profiler-build/v2.11.0/) and [CET 1.38.1 port notes](docs/CET-1.38.1-PUBLIC-PORT.md).
3. [BUILDING.md](BUILDING.md), including the tracked-versus-staged manifest distinction.
4. [Native-only build workflow](.github/workflows/native-profiler-rebuild.yml), [2.5 package workflow](.github/workflows/cet-2.5-profiler-package.yml), and [v1.0.3 publisher](.github/workflows/publish-cet-2.5-v1.0.3.yml).
5. [Architecture and restore rules](docs/ARCHITECTURE.md).
6. The **staged** `MANIFEST.json` extracted from the canonical v1.0.3 ZIP, and the public ZIP/ASI checksums above.
7. [Native CI #42437606920](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/42437606920) and [portable CI #42443720194](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/42443720194).

The custom CET binary, temporary file replacement, backup/restore, output collection, and optional supported 0-Engine Lua integration are documented profiling operations. The package is not a conventional loose-file gameplay mod.

## Reporting a security concern

Non-sensitive problems can be reported through the repository issue tracker.

For a potentially exploitable or sensitive security issue, avoid publishing proof-of-concept details in a public issue. Use GitHub's private vulnerability-reporting/security-advisory mechanism when available for the repository.
