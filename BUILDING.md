# Building G-CET Runtime Profiler

This document describes how the **public G-CET manager and launcher** are built from this repository and documents the provenance of the native CET profiler payload.

For **v1.0.3 targeting Cyberpunk 2077 2.5 / CET 1.38.1**, the authoritative workflows are:

- [Manual native-only rebuild](.github/workflows/native-profiler-rebuild.yml) — CET C++ compilation when the native source intentionally changes.
- [Verified 2.5 portable package](.github/workflows/cet-2.5-profiler-package.yml) — reuses the pinned native artifact and builds the manager/launcher.
- [v1.0.3 release publication](.github/workflows/publish-cet-2.5-v1.0.3.yml) — creates the clean public ZIP from the validated portable artifact.

The generic `.github/workflows/build.yml` is **not** the authoritative v1.0.3 publication path; historical 1.37.1 build information belongs to the earlier 2.31 release.

## Build scope and v1.0.3 native provenance

The repository provides source for:

- `G-CET-Runtime-Profiler.exe` — small native Windows launcher in `src/G.CETProfiler.Launcher/launcher.c`.
- `app/G-CET-Runtime-Profiler.App.exe` — .NET 8 WinForms application under `src/G.CETProfiler.App/`.
- `app/G.CETProfiler.Core.dll` — installation/restore, result collection and reporting logic under `src/G.CETProfiler.Core/`.
- Lua integrations under `payload/0-Engine/` and `payload/CETProfilerControls/`.
- Custom native CET profiler builder and source under `native-profiler-build/v2.11.0/`.

The **shipped v1.0.3 native ASI** is compiled from upstream CET 1.38.1 commit `213151fa22c79182922a08a44e4a44d44739d1cd`, using Xmake `3.1.1`, Windows SDK `10.0.26100.0`, and the Windows CI-selected MSVC toolset (see the native build logs). Although the builder directory name and one patch script filename retain old version labels, `Build-Profiler.ps1` on this branch explicitly pins CET 1.38.1. See [the 2.5 port documentation](docs/CET-1.38.1-PUBLIC-PORT.md).

```text
Native generating-source pin: 8b7f743c3aa9874cc16c58b2efa4ce950f5b6a1f
Native CI run:               42437606920
Native profiler SHA-256:      f06d00a684f57c93bcf90c9f8fbeb1c4ba56a9ada25904d98cee6a55621b370f
Tested official CET ASI:     f0acdad154bd0033583e0f6e675ea18b4bdb60cda28133d792a4674e6d7f228a
Portable package CI run:     42443720194
```

The official CET reference hash refers to the **specific CET ASI tested**, not an assertion that all independently packaged CET builds are byte-identical.

**Checked-in vs. shipped values:** the tracked `MANIFEST.json` and tracked `payload/cyber_engine_tweaks.PROFILER.dll` on this branch still contain older 1.37.1 baseline data. The [2.5 portable workflow](.github/workflows/cet-2.5-profiler-package.yml) retrieves the verified 1.38.1 native DLL from the pinned native CI run, checks its SHA-256, refuses changes to native generating sources since the source pin, replaces the staged payload, and sets the **release-staged** `MANIFEST.json` to `targetCET.version = 1.38.1`, native hash `f06d00...` and reference official CET hash `f0acda...`. The **manifest inside the published v1.0.3 ZIP** is authoritative for what ships. Routine C#/Lua/docs edits do not trigger native recompilation.

The earlier CET 1.37.1 historical reconstruction instructions apply to [v1.0.2](https://github.com/giorgioherma/G-CET_Runtime_Profiler/releases/tag/v1.0.2) and must not be used for Cyberpunk 2.5.

## Prerequisites

The 2.5 portable CI runs on Windows and uses:

- .NET 8 SDK
- Visual Studio / MSVC x64 C++ build tools
- Windows Resource Compiler (`rc.exe`)
- PowerShell
- Git

## Managed application

From the repository root:

```powershell
dotnet build G-CET-Runtime-Profiler.sln -c Release
```

The 2.5 portable workflow publishes the WinForms application as a self-contained x64 application:

```powershell
dotnet publish src/G.CETProfiler.App/G.CETProfiler.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o publish-app
```

Important release properties are declared in:

`src/G.CETProfiler.App/G.CETProfiler.App.csproj`

The application deliberately uses `PublishSingleFile=false`; the .NET runtime and managed assemblies remain visible under the package's `app\` directory.

## Native launcher

The source is:

`src/G.CETProfiler.Launcher/launcher.c`

The GitHub Actions workflow locates the Visual Studio x64 toolchain, compiles the resource file, and then builds the launcher using the equivalent of:

```cmd
rc.exe /nologo /fo"launcher.res" "src\G.CETProfiler.Launcher\launcher.rc"

cl.exe /nologo /O2 /MT /DUNICODE /D_UNICODE ^
  /Fo:"launcher.obj" ^
  "src\G.CETProfiler.Launcher\launcher.c" ^
  "launcher.res" ^
  /link /SUBSYSTEM:WINDOWS user32.lib ^
  /OUT:"G-CET-Runtime-Profiler.exe"
```

The launcher resolves the package directory, sets `G_CET_PROFILER_PACKAGE_ROOT`, and starts `app\G-CET-Runtime-Profiler.App.exe`. It does not contain the profiler implementation.

## Release staging and final package

The [2.5 portable CI](.github/workflows/cet-2.5-profiler-package.yml) stages the root launcher, self-contained .NET 8 application, 1.38.1 hash-locked profiler DLL, updated manifest, Lua integrations and illustrated installation instructions. It exercises installation, collection, restoration and Scheduler checks. Its **intermediate test artifact** may contain optional validation fixtures.

The [v1.0.3 publisher](.github/workflows/publish-cet-2.5-v1.0.3.yml) calls [the clean release packager](scripts/package-cet-2.5-release.py) to produce the final public ZIP **without rebuilding CET**:

```text
G-CET-Runtime-Profiler-v1.0.3-CP2077-2.5-CET-1.38.1.zip
├─ Install_Instructions.html
└─ G-CET-Runtime-Profiler/
   ├─ G-CET-Runtime-Profiler.exe
   ├─ G-CET-Runtime-Profiler.ico
   ├─ MANIFEST.json        # staged CET 1.38.1 values
   ├─ VERSION.txt
   ├─ app/                 # self-contained .NET 8 application/runtime
   ├─ payload/             # verified CET DLL and Lua integrations
   └─ RESULTS/
```

The final ZIP excludes developer documentation, validation mods, redundant nested archives, and internal checksum sidecars. The release ZIP checksum is a **separate GitHub release asset**.

## Hash verification

[Published v1.0.3 release](https://github.com/giorgioherma/G-CET_Runtime_Profiler/releases/tag/v1.0.3):

```text
ZIP:     G-CET-Runtime-Profiler-v1.0.3-CP2077-2.5-CET-1.38.1.zip
SHA-256: 6d729fb85c389cb310967c6805a44ebd7517394aeefd616fb7de75278ecda86c
ASI:     f06d00a684f57c93bcf90c9f8fbeb1c4ba56a9ada25904d98cee6a55621b370f
```

Verify downloaded and extracted files with PowerShell:

```powershell
Get-FileHash '.\G-CET-Runtime-Profiler-v1.0.3-CP2077-2.5-CET-1.38.1.zip' -Algorithm SHA256
Get-FileHash '.\G-CET-Runtime-Profiler\payload\cyber_engine_tweaks.PROFILER.dll' -Algorithm SHA256
```

Compare the ZIP hash to its separately published `.zip.sha256.txt`, and the extracted DLL hash to the pinned native ASI hash. Nexus Mods and VirusTotal reports about the **ZIP** must be matched against its ZIP hash, not against the different DLL hash.

## Reproducibility and review

The manager and launcher source and exact build workflows are public. [Native build #42437606920](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/42437606920) establishes the compiled 1.38.1 ASI provenance. [Portable CI #42443720194](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/42443720194) verifies the pinned source/binary, staged package, install/collect/restore behavior and Scheduler regression checks. The release publisher independently checks the native DLL hash while assembling the final v1.0.3 ZIP.

Native compilation and game-runtime validation are separate forms of evidence. The [v1.0.3 release notes](docs/releases/CET25-v1.0.3.md) describe the actual in-game test configuration and results; they do not guarantee compatibility with every third-party mod.

For security and moderation review, see [SECURITY.md](SECURITY.md), [the CET 1.38.1 port documentation](docs/CET-1.38.1-PUBLIC-PORT.md) and the canonical [v1.0.3 release](https://github.com/giorgioherma/G-CET_Runtime_Profiler/releases/tag/v1.0.3).
