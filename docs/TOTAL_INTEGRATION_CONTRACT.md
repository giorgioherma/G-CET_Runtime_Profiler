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
