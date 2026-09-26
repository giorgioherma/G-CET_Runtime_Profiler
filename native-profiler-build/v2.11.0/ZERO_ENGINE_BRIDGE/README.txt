0-Engine v2.11 profiler bridge

Target:
bin\x64\plugins\cyber_engine_tweaks\mods\0-Engine\modules\Scheduler.lua

Baseline SHA256:
dcefdb78ad7983124bcb7f5389484437ad259db1d3615aef63b43b50b488c382

Bridge SHA256:
df93a59dc6971352902a1198735fa58424b21c76f47446d1733467719fcbed61

The bridge changes profiler observability only:
- registers Scheduler owner/job identity once
- times each executed job only while CET profiler capture is RUNNING
- begins/ends a Scheduler-frame telemetry group
- does not alter cadence, staggering, callbacks, pause policy or catch-up

If CET profiler v2.11 API is unavailable, the bridge remains dormant.
