# Security and Binary Provenance

This document is intended for users, antivirus/security reviewers, and platform moderators reviewing the G-CET Runtime Profiler portable Windows package.

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
| `payload\cyber_engine_tweaks.PROFILER.asi` | Custom-built native profiler created through a PowerShell-based reconstruction/build process; exact release bytes are version/hash locked by `MANIFEST.json` |

The complete manager/launcher build procedure is documented in [BUILDING.md](BUILDING.md).

## Native profiler ASI provenance

The shipped native profiler payload is:

```text
File:                  payload\cyber_engine_tweaks.PROFILER.asi
Native profiler ver.:  2.11.0
Target CET ver.:       1.37.1
SHA-256:               011a9d3fc908e7cce3309ac5b4520ba9a3db5ad301edc6d2a465ca4c77486768
```

The v2.11.0 ASI was built through a recovered **PowerShell-based historical CET reconstruction pipeline**. The builder clones official Cyber Engine Tweaks source at commit `61bd6214f0f5f8748589c9e476538614a13908c0`, reconstructs the historical xmake package state, pins Xmake 3.0.3, MSVC 14.44 / compiler 19.44 and Windows SDK 10.0.26100.0, applies the profiler source patch, and compiles the resulting CET ASI.

The builder records the official CET v1.37.1 reference SHA-256 as:

```text
43a7b94698979703f9dfa2b0950607c9a88f53b84bbf09a8e593c7e218d41059
```

That value matches `targetCET.officialSha256` in the current G-CET `MANIFEST.json`.

The public manager GitHub Actions workflow does not re-run this historical native reconstruction for every manager build. Instead, the completed ASI is committed as a release input, `MANIFEST.json` records its expected profiler SHA-256, and CI fails if the shipped bytes differ.

The recovered builder contents and file hashes are documented in [docs/NATIVE_PROFILER_BUILD_PROVENANCE.md](docs/NATIVE_PROFILER_BUILD_PROVENANCE.md).

## Package controls

The release workflow performs checks intended to make the public package inspectable and deterministic:

- verifies the hash-locked profiler ASI and integration payloads before building;
- builds the public manager from repository source;
- builds the native launcher from repository source;
- uses a normal ZIP rather than a self-extracting package;
- keeps `PublishSingleFile=false` so managed/runtime components remain visible;
- rejects public `.ps1`, `.vbs`, and `.cmd` manager scripts;
- removes public PDB files;
- verifies expected package paths before release staging;
- publishes a SHA-256 checksum beside the GitHub release ZIP.

## File safety

The lifecycle core follows a preserve-before-mutate rule.

Before replacing a supported user-owned file, G-CET stores and verifies the recoverable original state. Restore validates managed state before replacing live files. Unexpected changes cause normal restore to stop rather than overwrite an unknown state.

Collected CET profiler output is copied and hash-verified before profiler-owned live output is removed. Unrelated files are not intentionally swept.

External frame-time captures are copy-only; the source capture owned by the external profiler is not moved or deleted.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the detailed transaction and recovery model.

## v1.0.0 canonical release identity

Canonical GitHub release ZIP:

`G-CET-Runtime-Profiler-v1.0.0.zip`

SHA-256:

```text
0fb6adee34a689273bcb4be17b987656b395c5308dca487d2e425e58cf2d7b87
```

A scanner or moderation report for a different SHA-256 is reviewing different bytes and should not be treated as the identity of this canonical release.

## Notes for Nexus Mods / security reviewers

For a compiled-code review, the relevant public material is:

1. repository source under `src/`;
2. [BUILDING.md](BUILDING.md);
3. the canonical CI definition at `.github/workflows/build.yml`;
4. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md);
5. `MANIFEST.json`, which locks the native profiler/integration payload identities;
6. the canonical GitHub release ZIP and its published SHA-256 file.

The manager and launcher can be traced directly to source in this repository. The native profiler ASI should be reviewed as a custom source-built CET binary produced by the pinned historical reconstruction pipeline described above and in [docs/NATIVE_PROFILER_BUILD_PROVENANCE.md](docs/NATIVE_PROFILER_BUILD_PROVENANCE.md).

## Reporting a security concern

Non-sensitive problems can be reported through the repository issue tracker.

For a potentially exploitable or sensitive security issue, avoid publishing proof-of-concept details in a public issue. Use GitHub's private vulnerability-reporting/security-advisory mechanism when available for the repository.
