# Native Profiler v2.11.0 Build Provenance

This document records the recovered source/build package used for the native profiler shipped by G-CET Runtime Profiler.

## Published clean source

The recovered builder/source is published as ordinary inspectable text files at:

[**native-profiler-build/v2.11.0/**](../native-profiler-build/v2.11.0/)

The published tree intentionally contains **no compiled ASI/EXE/PDB binaries** and excludes unrelated end-user install/collection helpers. It contains the native builder, profiler implementation, CET source patcher, historical-toolchain reconstruction helpers, validation records, changeset, and 0-Engine Scheduler bridge evidence.

After upload, the Git blob identities and byte sizes of the 14 recovered source files were compared against the original recovered files. They match byte-for-byte.

## What was recovered

The recovered package is:

`CET_Native_Runtime_Profiler_v2.11.0_SCHEDULER_ATTRIBUTION`

It contains the native build/reconstruction pipeline rather than only a compiled ASI.

Key build-source files:

- `Build-Profiler.ps1`
- `CETRuntimeProfiler.h`
- `Patch-CET-v1.37.1.ps1`
- `Patch-CET-HistoricalBuild.ps1`
- `Check-Historical-Toolchain.ps1`
- `Get-CETPEInfo.ps1`
- `Verify-Patched-Source.ps1`
- `HISTORICAL_BUILD_FACTS.txt`
- `V2.11.0_CHANGESET.diff`
- `V2.11.0_NOTES.txt`
- `V2.11.0_VALIDATION.txt`
- `ZERO_ENGINE_BRIDGE/Scheduler.diff`

## Reconstructed upstream source

`Build-Profiler.ps1` clones official Cyber Engine Tweaks and checks out exactly:

```text
61bd6214f0f5f8748589c9e476538614a13908c0
```

The builder identifies this as the CET v1.37.1 source revision.

The historical build facts record the official reference binary as:

```text
SHA-256: 43a7b94698979703f9dfa2b0950607c9a88f53b84bbf09a8e593c7e218d41059
Length:   7,391,744 bytes
Linker:   14.44
```

The SHA-256 is identical to `targetCET.officialSha256` in G-CET's current `MANIFEST.json`.

## Historical toolchain reconstruction

The builder pins/reconstructs:

```text
CET source commit: 61bd6214f0f5f8748589c9e476538614a13908c0
Xmake:             3.0.3
MSVC toolset:      14.44
Compiler family:   19.44
Windows SDK:       10.0.26100.0
```

It reconstructs the xmake-repo state immediately before the known official CET release workflow time:

```text
2025-09-28T10:48:33Z
```

and isolates the xmake package/global caches from newer local toolchains.

## Build sequence

The recovered builder performs two phases.

### Phase A — historical control build

1. Clone the official CET repository.
2. Checkout the pinned CET source commit.
3. Initialize CET submodules.
4. Attach the historical xmake-repo snapshot.
5. Configure xmake with the pinned MSVC toolset and SDK.
6. Build an unmodified historical control ASI.
7. Verify that the resulting PE linker is 14.44.

### Phase B — profiler build

1. Apply `Patch-CET-v1.37.1.ps1`.
2. Copy `CETRuntimeProfiler.h` into the CET scripting source tree.
3. Patch the required CET scripting files for callback profiling and Scheduler attribution.
4. Clean the historical build.
5. Recompile CET.
6. Copy the resulting binary as `cyber_engine_tweaks.PROFILER.asi`.
7. Verify the resulting profiler PE linker is 14.44.

The source patch refuses to run when the checked-out CET revision differs from the pinned commit.

## Shipped profiler identity

The current G-CET manifest records:

```text
Native profiler version: 2.11.0
Target CET version:      1.37.1
Profiler ASI SHA-256:    011a9d3fc908e7cce3309ac5b4520ba9a3db5ad301edc6d2a465ca4c77486768
```

G-CET CI verifies this hash before staging a public release.

## Recovered source-package hashes

The recovered source files were independently hashed before documentation:

```text
70fcf0a1fbcfebe9e62f1feceacbbe19097fff3d52ab8dc2187cad6f16480948  Build-Profiler.ps1
6d0392242d2a90dd4dfb45967da76320922977deaa18641123d1053ec1af1cf7  CETRuntimeProfiler.h
7da4338efade9526ec7f6e5dadb16f544bd8c4fe15c7eedd74e6571bf46367a6  Check-Historical-Toolchain.ps1
4b3c5251631d7c9182adaf7ebbe44eb50d7e3e7f70424bfefd3e2f511ad3d51d  Get-CETPEInfo.ps1
5117167b1945ba7e930ddc66a02de34ccaec52d8356a0864fa642f03328c244a  HISTORICAL_BUILD_FACTS.txt
39710cbe1869e484123e801cda0ba35cd4a3a92249d23716abaaacacea4e5f93  Patch-CET-HistoricalBuild.ps1
463e3169e845f8baf66e1db6d13328daaef6cdc164769c51272e3c642ca76609  Patch-CET-v1.37.1.ps1
0094307c17b0647e5791e176ac3f1c930478d89c726c853151564c5680ab696a  Verify-Patched-Source.ps1
d25bf12c0f38fa1e884c19f0d10ef6bcb2c5395cf25980da63dad7f6ba9b7726  V2.11.0_CHANGESET.diff
47f37844914f2ac262dc8682d36cbf3a60b834c8c1bcc9a2f3533a6956bcbfb0  V2.11.0_NOTES.txt
124d382bd9ad3ba680906f6ec7c8e8e13f5e6e0531a08be9da09b9d3c9c26e17  V2.11.0_VALIDATION.txt
8909382adf6b47185591933b6afb2364afd0681d6e4fefc5d7c41d342b35a88d  ZERO_ENGINE_BRIDGE/Scheduler.diff
```

## Validation note

The recovered v2.11.0 validation record states that the profiler header compiled as C++20 in a standalone harness and that synthetic Scheduler telemetry generated the expected Scheduler CSV outputs. The authoritative full CET compile path remains `Build-Profiler.ps1` on the pinned Windows historical toolchain.

This provenance document intentionally distinguishes:

- the source/reconstruction process used to build the native profiler;
- the already-built ASI that G-CET ships and hash-verifies;
- the separate C# manager/launcher build performed by the current G-CET GitHub Actions workflow.
