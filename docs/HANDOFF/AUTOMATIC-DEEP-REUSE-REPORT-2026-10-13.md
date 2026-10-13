# Automatic deep data-reuse investigation in reports (2026-10-13)

## Implementation

The normal managed `ResultReportService.Generate(captureRoot)` now reads native v2.15 `CET_Runtime_Profile_Deep_Functions.csv`, `Deep_Callsites.csv`, and `Deep_Registrations.csv` **without extra game hooks or external Python**. It automatically adds `deepDataReuseCandidates` to both `CET_Summary.json` and `CET_Resolver_Input.json`, plus a human table under **Data reuse investigation: sampled producers and callers** in `CET_Report.html`.

- New analyzer: `src/G.CETProfiler.Core/Services/ResultReportService.DeepReuse.cs`.
- Existing report paths wired in `ResultReportService.cs`, `ResultReportService.Analysis.cs`, `ResultReportService.Resolver.cs`, and `ResultReportService.Html.cs`.
- Regression contract: `tests/DeepReuseReportContract.ps1`, invoked from Windows CI `.github/workflows/build.yml`.
- Older captures missing deep CSVs explicitly show `NOT_CAPTURED` and preserve all previous JSON and metrics.
- Original Python CLI remains an optional research utility; it is **not** required for capture or report generation.

## Source attribution semantics

Callsite → sampled child function join uses **(`RegistrationId`, `ProfileEpoch`, `ChildFunctionKey = FunctionKey`)**. Owner/mod names use stable registration ID because registration metadata profile epoch may differ. Ambiguous function-key collisions and conflicting owner IDs are excluded from ownership claims and surfaced in coverage.

For each function we report total native sampled function-own positive allocation bytes/operations **once**. Each caller has its own sampled invocation-edge calls, frames, and invocation ordinals, but **no allocated bytes assigned to an individual caller**. Samples remain bounded, nested inclusive timings are not savings, and owner-0 is not attributed. We do not equate positive allocator growth with garbage or retained heap.

The report contains candidate lists sorted by sampled function own allocations and by sampled invocation edges. Cross-mod data equivalence, actual table-content update frequency, receiver/input equality, mutation/escape ownership, freshness and invalidation remain **NOT_MEASURED / NOT_PROVEN**; all candidates are `INVESTIGATE_ONLY` with `safeToShare=false`, `estimatedAvoidableBytes=null` and `authorizesAutomaticRewrite=false`. **Zero proven shared-cache groups.** This stage is proof collection only, not new mod/Resolver transformations.

## CI / user test

Windows CI exercises legacy reports and a fixture with multi-caller one-producer joins, duplicate function IDs, nonmatching sample epochs, registration metadata epoch drift, and no double-counting. Review the latest workflow result before shipping.

For an already extracted capture, generate reports using the portable Profiler app: `app\G-CET-Runtime-Profiler.App.exe --report --capture "C:\path\to\capture" --json`. A new normal capture report includes the section automatically. No extra Python installation, profiler DLL replacement or changes to current CET mods.

## Following engineering stage

Source-validated selected in-situ data probes (only existing getter calls), actual immutable result/parameter stability, writer/invalidation/mutable alias proofs and distinct cross-mod consumers. Do not auto-share native tables or global-cache by API name. Native 2.15 payload SHA remains pinned and previously crashing native 2.16 is not promoted.
