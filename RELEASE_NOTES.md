# G-CET Runtime Profiler v1.0.0

First stable release of the standalone G-CET Runtime Profiler.

## Highlights

- Portable, self-contained Windows ZIP — extract and run.
- CET F11 START / F11 STOP + automatic export.
- Transactional CET install and exact restore.
- Optional 0-Engine Scheduler attribution with safe fallback.
- Optional frame-time companion; CapFrameX is recommended/tested but not required.
- External frame-time results are copy-only.
- CET live profiler files are verified, collected, and cleared from the game folder.
- Unified second-page readiness/status UI.
- Package-local settings and results.
- Headless JSON interface for the exact same standalone package consumed by TOTAL Profiler.

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
