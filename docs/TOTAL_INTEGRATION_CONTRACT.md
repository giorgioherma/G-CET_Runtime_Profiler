# TOTAL Profiler integration contract

`G-CET-Runtime-Profiler` is a standalone product first. TOTAL Profiler consumes the exact standalone release unchanged.

TOTAL must not:

- maintain its own CET install/restore implementation;
- inject its own 0-Engine bridge;
- own CETProfilerControls;
- write CET key bindings itself;
- redirect CET's standalone result folder;
- delete standalone CET result archives after collection.

TOTAL may:

- call the standalone EXE headlessly;
- verify that CET is installed and bound to F11;
- ask CET to collect the completed capture;
- copy the completed CET result into TOTAL's combined `Raw/CET` package;
- correlate CET + GRSP + CapFrameX;
- generate TOTAL's combined HTML / ZIP.


## Dependency rule

TOTAL should consume a versioned standalone release of this repository and invoke `G-CET-Runtime-Profiler.exe` headlessly. The files used by TOTAL must be byte-identical to the standalone package for that release.

Combined-capture conveniences belong in TOTAL. If TOTAL needs a new CET capability, that capability should first be exposed by the standalone core/headless contract rather than implemented as a TOTAL-only fork.
