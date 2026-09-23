# G-CET-Runtime-Profiler

Standalone CET/Lua runtime profiler manager for Cyberpunk 2077, with optional 0-Engine scheduler attribution.

## Repository status

This repository seed begins the **C#/.NET 8 port** of the existing `3.0.0-alpha6c` development manager.

The product design is intentionally **not being changed during the port**. The existing PowerShell implementation is kept under `reference/powershell-alpha6c/` only as the behavior/reference implementation. It is not the intended final public frontend.

## Final product

The public package will contain a normal transparent C# WinForms executable:

```text
G-CET-Runtime-Profiler.exe
payload/
MANIFEST.json
RESULTS/
```

The same executable also exposes headless JSON commands so G's Cyberpunk 2077 TOTAL Profiler can use the exact standalone product as a dependency.

## Capture contract

```text
F11 #1 -> START fresh capture
F11 #2 -> STOP + AUTO EXPORT CSV
```

CET owns the key binding, profiler lifecycle, 0-Engine integration, backups, restore state, and standalone results. TOTAL Profiler owns only multi-profiler collection/correlation.

## Current payload

The current alpha6c native profiler payload is preserved under `payload/`:

- `cyber_engine_tweaks.PROFILER.asi`
- `CETProfilerControls/init.lua`
- `0-Engine/modules/Scheduler.lua`
- `0-Engine/modules/CETProfilerScheduler.lua`

## Port plan

1. Port status/path/hash/manifest models to `G.CETProfiler.Core`.
2. Port persistent state and binding transaction.
3. Port CET ASI install/restore.
4. Port 0-Engine compatibility detection + injection/restore.
5. Port collect/reset result handling.
6. Wire the existing standalone WinForms design to the C# core.
7. Complete the headless JSON contract.
8. Add integration/restore fixtures and release packaging.

See `docs/PORT_CONTRACT.md` before changing behavior.
