# G-CET Runtime Profiler v1.0.0

First stable release of the standalone G-CET Runtime Profiler.

## Highlights

- Portable, self-contained Windows ZIP — extract and run.
- CET F11 START / F11 STOP + automatic export.
- Transactional CET install and exact restore.
- Optional 0-Engine Scheduler attribution with safe fallback.
- Optional frame-time companion; CapFrameX **1.9.1.2 Beta** is the tested reference build, but frame-time pairing is not required.
- External frame-time results are copy-only.
- CET live profiler files are verified, collected, and cleared without sweeping unrelated CET files.
- Adaptive human-readable diagnosis in `CET_Report.html`, with the same condensed evidence available in `CET_Summary.json`.
- Interactive synchronized frametime/CET graph with **Shift + mouse-wheel zoom**.
- Final dark G-CET UI with semantic marker colors, clearly gray disabled actions, cyan enabled forward actions, and magenta restore action.
- Final multi-size G-CET Windows icon for both the native launcher and managed application.
- Package-local settings and results.
- Headless JSON interface for the exact same standalone package consumed by TOTAL Profiler.

## UI status convention

Status prose remains white. Only the semantic markers carry state color:

- ✅ green — confirmed good / completed
- ⚠️ yellow — optional, pending, unknown, or degraded-but-usable
- ❌ red — blocking error requiring attention

Unavailable action buttons are gray. Available normal actions are cyan. **RESTORE ORIGINAL STATE** is magenta when available.

## Frame-time companion

G-CET works standalone.

CapFrameX **1.9.1.2 Beta** is the exact development/test reference for v1.0.0. The application links to the upstream CapFrameX releases page without treating the upstream page's displayed version label as authoritative for the tested executable build.

Other frame-time profilers can still be used. Recognized CapFrameX JSON receives the full synchronized analysis layer; unrecognized companion files are still preserved safely under `FrameTime\`.

## Portable package

```text
G-CET-Runtime-Profiler-v1.0.0.zip
└─ G-CET-Runtime-Profiler\
   ├─ G-CET-Runtime-Profiler.exe
   ├─ MANIFEST.json
   ├─ VERSION.txt
   ├─ app\
   │  ├─ G-CET-Runtime-Profiler.App.exe
   │  ├─ G-CET-Runtime-Profiler.App.deps.json
   │  ├─ G-CET-Runtime-Profiler.App.runtimeconfig.json
   │  ├─ G.CETProfiler.Core.dll
   │  └─ .NET runtime files...
   ├─ payload\
   ├─ RESULTS\
   └─ docs\
      ├─ README.md
      ├─ CHANGELOG.md
      └─ RELEASE_NOTES.md
```

The root EXE is a small native launcher. The managed application and self-contained .NET runtime live under `app\`; profiler payload, manifest, results, and documentation remain separate at the package root.

Extract to a writable folder and run `G-CET-Runtime-Profiler.exe`.

## Safety and verification

- The profiler payload is hash-locked and verified by CI.
- Installation and restore are transactional.
- Original user state is preserved before managed mutation.
- Core-only fallback leaves unsupported 0-Engine layouts untouched.
- Known live result files are copied and verified before clearing.
- Emergency restore preserves unresolved recovery state instead of guessing.
- CI exercises launcher handoff, package layout, install/collect/restore, emergency restore, 0-Engine resolver/handoff, report generation, and release staging.

The GitHub release publishes the exact portable ZIP produced from the final v1.0.0 source and a matching SHA-256 text file.

## Frozen canonical build

- Source commit: `ec0bfd0954ce10d39eebe86f38ef3f14e21f65f4`
- GitHub Actions run: `36169889066`
- Frozen artifact ID: `10880038160`
- Canonical release asset: `G-CET-Runtime-Profiler-v1.0.0.zip`
- SHA-256: `8f35cb5f2044ad9dc2ab8352d761b7fe689ac53c4894c8067e10fdf71a7a27d0`

This exact artifact is the frozen v1.0.0 public build. GitHub Releases is the canonical download surface; later repository-only documentation or metadata changes do not redefine the v1.0.0 binary.
