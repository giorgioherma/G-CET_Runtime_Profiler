# CET Native Runtime Profiler v2.11.0 — recovered build source

This directory contains the **clean, unpacked native build/reconstruction source** used for the G-CET v2.11.0 profiler payload.

It is published here so users, security scanners, and Nexus Mods reviewers can inspect the native profiler build chain directly in GitHub.

## No compiled binaries in this directory

This source tree intentionally excludes:

- `cyber_engine_tweaks.OFFICIAL.asi`
- compiled profiler ASI files
- PDB/build outputs
- end-user install/restore/collection helper scripts that are not required to reconstruct the native binary

The G-CET release continues to ship the finished, hash-locked native profiler under the PE-accurate package filename:

`payload/cyber_engine_tweaks.PROFILER.dll`

During installation those same verified bytes are copied to CET's required live filename `cyber_engine_tweaks.asi`.

## Pinned upstream and toolchain

```text
CET source commit:      61bd6214f0f5f8748589c9e476538614a13908c0
Target CET version:     1.37.1
Official CET SHA-256:   43a7b94698979703f9dfa2b0950607c9a88f53b84bbf09a8e593c7e218d41059
Xmake:                  3.0.3
MSVC toolset:           14.44 / compiler 19.44
Windows SDK:            10.0.26100.0
Profiler version:       2.11.0
Shipped profiler SHA:   011a9d3fc908e7cce3309ac5b4520ba9a3db5ad301edc6d2a465ca4c77486768
```

The official CET SHA above matches `targetCET.officialSha256` in the repository `MANIFEST.json`. The shipped profiler SHA matches `targetCET.profilerSha256`.

## Main files

- `Build-Profiler.ps1` — authoritative Windows reconstruction/build pipeline.
- `CETRuntimeProfiler.h` — native profiler implementation.
- `Patch-CET-v1.37.1.ps1` — applies the profiler integration to the pinned CET source revision.
- `Patch-CET-HistoricalBuild.ps1` — attaches the historical xmake package snapshot.
- `Check-Historical-Toolchain.ps1` — validates the required historical MSVC/SDK environment.
- `Get-CETPEInfo.ps1` — PE/linker inspection helper.
- `Verify-Patched-Source.ps1` — source-patch verification helper.
- `HISTORICAL_BUILD_FACTS.txt` — recovered official build facts.
- `V2.11.0_CHANGESET.diff` — changes from the preceding profiler checkpoint.
- `V2.11.0_NOTES.txt` and `V2.11.0_VALIDATION.txt` — implementation and validation records.
- `ZERO_ENGINE_BRIDGE/` — Scheduler attribution bridge diff and provenance.

## Build outline

On a compatible Windows development environment, `Build-Profiler.ps1`:

1. verifies MSVC 14.44 and Windows SDK 10.0.26100.0;
2. downloads/pins Xmake 3.0.3;
3. reconstructs the xmake-repo state from the historical CET release date;
4. clones official Cyber Engine Tweaks;
5. checks out CET commit `61bd6214f0f5f8748589c9e476538614a13908c0`;
6. initializes the CET submodules;
7. builds an unmodified historical control CET ASI and checks linker 14.44;
8. applies `Patch-CET-v1.37.1.ps1` and `CETRuntimeProfiler.h`;
9. rebuilds CET as `cyber_engine_tweaks.PROFILER.asi`;
10. verifies the profiler PE linker is 14.44.

For the full provenance and relationship to the G-CET release, see:

- [Native profiler build provenance](../../docs/NATIVE_PROFILER_BUILD_PROVENANCE.md)
- [Building G-CET](../../BUILDING.md)
- [Security and binary provenance](../../SECURITY.md)

## Recovered-source integrity

The published source files in this directory were compared against the recovered clean source bundle using Git blob identities after upload. The source files are byte-for-byte identical to the recovered originals.

The clean review bundle created from the recovered package has SHA-256:

```text
f2c7e3b5274fddf9595394917033db2a9afa9c6b1239a9b7619a8bd843b38093
```
