# New chat — minimal continuation prompt

Copy this into a new development conversation. Add the current task, capture(s) or original/live mod archives beneath it when relevant.

> Continue `giorgioherma/G-CET_Runtime_Profiler` on branch `dev/resolver-profiler-handoff`. **Before doing anything else, read the current `docs/HANDOFF/CORE.md`, then `BUILD-PIPELINE.md`, `PRODUCT-STATUS.md`, `PROJECT-CONTRACT.md` and relevant recent `ACTIVE-STATE.md` entries.** Verify current GitHub HEAD, `MANIFEST.json`, product version, and **both native/main Actions builds**. The core has the current snapshot; the long historical entries may refer to obsolete releases.
>
> **Strict CI division:** `.github/workflows/native-profiler-rebuild.yml` builds/tests/publishes **only the native DLL+checksum**. `.github/workflows/build.yml` imports a pinned native artifact with verified SHA, and builds/packages/tests **both Profiler and standalone Resolver**. To ship new native: native CI green → update SHA/run pin + manifest/version → main build green → verify both artifacts. Track each run to terminal outcome; repair and rerun failures. Do **not** deliver a combined package from native CI or say that native CI alone updates the main Profiler.
>
> Product: portable CET profiler with safe install/restore, timing/burst/deep and G-GC reports, optional scalar probe; one Resolver running source-proven generic then semantic optimization on the user's original/current mod source, generating a non-destructive ZIP. Preserve author gameplay, callback cadence/lifecycle, version-fluid matching, composition and idempotence. GC/provenance is research evidence, **not permission to rewrite**. Resolve requested task from actual source/capture and run appropriate tests; CI does not prove in-game correctness. Keep the **short core** current and put deep findings in their own technical docs.
>
> **Task for this chat:** [state the specific task here].

See [CORE.md](CORE.md) for the one-minute app/progress overview and verified current build; do **not** copy an obsolete hardcoded version into this reusable prompt.
