# G-CET build workflow and release policy

> **MANDATORY NATIVE REBUILD COST CONTRACT — 2026-11-08 (user-approved):** Native CET recompilation is never a routine CI step. Both the v2.31 development and isolated Cyberpunk 2.5 profiler native workflows use **manual `workflow_dispatch` only**. A C#/Lua/Scheduler/0-Engine/UI/report/test/documentation change must reuse the existing SHA256-pinned, previously successful ASI and must never trigger native rebuilding. The native job checks actual native generating-source deltas relative to the source commit of the currently pinned compiled artifact, and refuses unchanged sources. Native revisions are batched and compiled deliberately once; after an intentional successful native build, its source commit, artifact run and SHA256 are changed together in the consuming package workflow. Unverified native revisions must never replace a pinned DLL automatically. Product packaging is separate, must not invoke CET compilation, and must continue to fail closed if artifact bytes drift.
>

This is a **version-independent process**. Never hardcode recent run IDs, DLL checksums or release versions here. Read current workflow YAML, manifest and artifacts to learn their latest values.

## Native-only CI: compiled DLL

`.github/workflows/native-profiler-rebuild.yml` — **Rebuild G-CET native profiler**.

- **Only when manually dispatched AND generating native inputs have changed since the pinned compiled source commit**, reconstruct the pinned CET/LuaJIT source, compile and verify `cyber_engine_tweaks.PROFILER.dll`. No automatic rebuild on native path pushes, other pushes, docs, workflow changes or tests.
- Upload the player-consumed **DLL plus its actual SHA256.txt** in the native payload artifact. During attribution experiments, a **separate developer-only PDB artifact** may be published alongside its own SHA256 and the paired DLL SHA. The PDB must come from the same linker invocation, must never be included in normal Profiler/Resolver downloads, and must not replace/hash-bypass the pinned player DLL.
- **Never** build or upload managed Profiler or Resolver packages here.
- Native source changes are not automatically product version changes. A native-only success does not update the normal Profiler download.

## Main CI: Profiler and Resolver

`.github/workflows/build.yml` — **Build G CET Runtime Profiler**.

1. Check coordinated `VERSION.txt`, `Directory.Build.props` and `MANIFEST.json.packageVersion`.
2. Download the **pinned successful native CI artifact**. Cross-check the DLL SHA with its native checksum and `MANIFEST.json.targetCET.profilerSha256`. Never substitute the tracked old `payload/` DLL.
3. Verify native feature/report markers and hash-locked payload; compile/test shared core, Profiler GUI/CLI/launcher and Resolver; run source/semantic/restore contracts.
4. Package and verify **both** same-version artifacts: `G-CET-Runtime-Profiler-vX.Y.Z` and `G-CET-Resolver-vX.Y.Z`.
5. Follow all jobs through **completed success** and verify both artifacts exist. Test runtime, world load, capture, stop, input and restore separately when new native code is involved. CI cannot prove gameplay stability.
6. Ordinary CI must not create/overwrite official releases or tags outside the explicitly authorized release path.

## Integrating a new native build

- First land native source/contracts and complete native CI successfully.
- Read the checksum from the **actual successful output**, not earlier attempts.
- In a coordinated main-build commit, pin that native run/artifact, update manifest native identity/hash and add compiled-feature checks.
- Change the shared product version only when the user requests it or accepts a meaningful milestone; when authorized, synchronize **all three** version fields together.
- Run main CI, verify native import and **both packages**, preserve original CET and 0-Engine transaction/restore behavior.
- Record exact engineering run IDs/SHAs in [history](../history/ACTIVE-STATE.md), not this policy.

## Failure prevention

A compiled native ASI is an immutable reusable product input, not a CI side effect. Compile CET only for an intentionally approved actual native source revision; do not treat a native artifact as needing a rebuild due to unrelated code activity. Never confuse native-only workflow output with the user-ready Profiler. Verify artifact archive layout instead of assuming a nested ZIP. Historical source folder names are not authoritative version evidence. Do not declare queued builds successful. Document-only changes must not trigger native or app build runs.

Related: [build guide](../build/BUILDING.md), [native source provenance](../native/NATIVE_PROFILER_BUILD_PROVENANCE.md), [product status](../product/PRODUCT-STATUS.md).
