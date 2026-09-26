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
| `payload\cyber_engine_tweaks.PROFILER.asi` | Prebuilt native profiler input, version/hash locked by `MANIFEST.json` |

The complete manager/launcher build procedure is documented in [BUILDING.md](BUILDING.md).

## Native profiler ASI provenance

The shipped native profiler payload is:

```text
File:                  payload\cyber_engine_tweaks.PROFILER.asi
Native profiler ver.:  2.11.0
Target CET ver.:       1.37.1
SHA-256:               011a9d3fc908e7cce3309ac5b4520ba9a3db5ad301edc6d2a465ca4c77486768
```

The current repository **does not rebuild this ASI from the C# manager or native launcher source**.

It is committed as a prebuilt binary input. `MANIFEST.json` records the expected SHA-256 and the GitHub Actions workflow fails if the shipped bytes do not match that value.

This limitation is stated explicitly so that reviewers can distinguish reproducibly built application components from the prebuilt native profiler payload.

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

The manager and launcher can be traced directly to source in this repository. The native profiler ASI should be treated separately as the prebuilt, hash-locked binary input described above.

## Reporting a security concern

Non-sensitive problems can be reported through the repository issue tracker.

For a potentially exploitable or sensitive security issue, avoid publishing proof-of-concept details in a public issue. Use GitHub's private vulnerability-reporting/security-advisory mechanism when available for the repository.
