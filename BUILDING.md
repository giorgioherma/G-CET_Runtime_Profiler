# Building G-CET Runtime Profiler

This document describes how the **public G-CET manager and launcher** are built from this repository and identifies the one native profiler payload that is currently consumed as a prebuilt, hash-locked binary input.

The canonical automated build is:

`.github/workflows/build.yml`

## Build scope

The repository contains source for:

- `G-CET-Runtime-Profiler.exe` — small native Windows launcher in `src/G.CETProfiler.Launcher/launcher.c`
- `G-CET-Runtime-Profiler.App.exe` — .NET 8 WinForms application in `src/G.CETProfiler.App/`
- `G.CETProfiler.Core.dll` — lifecycle, install/restore, collection, and report logic in `src/G.CETProfiler.Core/`
- Lua integration payloads under `payload/0-Engine/` and `payload/CETProfilerControls/`

The repository also contains this prebuilt native payload:

- `payload/cyber_engine_tweaks.PROFILER.asi`

That ASI is **not produced by the .NET/launcher build described below**. It is treated as a versioned binary input and is SHA-256 locked by `MANIFEST.json` and verified by CI before a release is staged.

Current manifest identity:

```text
Native profiler version: 2.11.0
Target CET version:      1.37.1
Profiler ASI SHA-256:    011a9d3fc908e7cce3309ac5b4520ba9a3db5ad301edc6d2a465ca4c77486768
```

This distinction is intentional and is stated explicitly for security/moderation review. This repository does not claim that the ASI is reproducibly built from the C# or launcher source.

## Prerequisites

The canonical CI build runs on `windows-latest` and uses:

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

The release workflow publishes the WinForms application as a self-contained x64 application:

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

## Release staging

CI stages this layout:

```text
G-CET-Runtime-Profiler\
├─ G-CET-Runtime-Profiler.exe
├─ MANIFEST.json
├─ VERSION.txt
├─ app\
├─ payload\
├─ RESULTS\
└─ docs\
```

`Install_Instructions.html` is placed beside the product folder in the final ZIP.

The workflow also verifies that the public package does not contain `.ps1`, `.vbs`, or `.cmd` manager scripts.

## Hash verification

Verify a downloaded release with PowerShell:

```powershell
Get-FileHash ".\G-CET-Runtime-Profiler-v1.0.0.zip" -Algorithm SHA256
```

Canonical GitHub v1.0.0 release ZIP SHA-256:

```text
0fb6adee34a689273bcb4be17b987656b395c5308dca487d2e425e58cf2d7b87
```

The release also publishes a matching `.sha256.txt` asset.

## Reproducibility and review

For the manager/launcher, the source and exact build workflow are public in this repository.

For `cyber_engine_tweaks.PROFILER.asi`, the current repository provides the exact shipped binary, its version, its target CET version, and its locked SHA-256. It is presently a prebuilt input rather than an artifact rebuilt by this repository.

Security reviewers should therefore evaluate the manager/launcher source and CI build separately from the native profiler ASI provenance described in [SECURITY.md](SECURITY.md).
