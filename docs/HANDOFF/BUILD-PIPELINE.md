# G-CET build and release contract — TWO distinct workflows

**Owner:** the repository workflow definitions, not stale prose. **Updated 2026-10-14.** Read with [CORE.md](CORE.md) before any native or packaging task.

## 1 — Native CI builds ONLY the DLL

File: `.github/workflows/native-profiler-rebuild.yml`  
Displayed workflow: **Rebuild G-CET native profiler**

- Inputs: repository `native-profiler-build/v2.11.0/` scripts, `CETRuntimeProfiler.h`, LuaJIT GC patch, pinned CET 1.37.1 upstream source, historical Xmake/MSVC 14.44 toolchain. Historical source folder name does **not** equal current feature version.
- `Build-Profiler.ps1` reconstructs and compiles CET, then stages `cyber_engine_tweaks.PROFILER.dll`.
- Test DLL PE/linker and API/CSV source contract. Publish a native **DLL + SHA256.txt** artifact. Current artifact name is `gcet-native-experimental-scalar-observations` from historical successful [run #38473605694](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38473605694).
- **NOT its responsibility:** release manifest/package version bump, managing/compiling WinForms Profiler, packaging standalone Resolver, uploading portable Profiler ZIP, merging applications, releasing production artifacts.
- When source changes, trigger or dispatch the native workflow, check the final run **success** and download artifact hash. A build's previous status/sha is not sufficient for a newer source revision.

## 2 — Main CI builds and packages the Profiler AND Resolver

File: `.github/workflows/build.yml`  
Displayed workflow: **Build G CET Runtime Profiler**

1. Read coordinated `VERSION.txt`, `Directory.Build.props` and `MANIFEST.json` versions.
2. Download the **exact pinned native CI run + artifact** into `native-from-ci/` (current run #38473605694, artifact `gcet-native-experimental-scalar-observations`). Do **not** assume local `payload/cyber_engine_tweaks.PROFILER.dll` is the latest binary—it is overridden from native CI during packaging.
3. Verify native DLL SHA-256 against both its native `SHA256.txt` and `MANIFEST.json.targetCET.profilerSha256`; copy into `payload/` in this build's workspace only.
4. Enforce native expected symbol/CSV contracts (G-GC, deep, optional scalar) and hash-locked 0-Engine/controls payload.
5. Build .NET shared core, Profiler GUI/CLI, native root launcher and standalone Resolver. Run data/report, semantic, resolver, native payload/install/restore and packaging contracts.
6. Package and upload **two separate same-version artifacts**: `G-CET-Runtime-Profiler-vX.Y.Z` (full portable Profiler app + latest verified native DLL) and `G-CET-Resolver-vX.Y.Z` (standalone Resolver). The main CI may also perform guarded canonical release steps on release-eligible refs; inspect current triggers.
7. Verify the **main** CI success, both artifacts actually present, and new embedded native hash before saying “Profiler contains the new native.”

**Current verified main run:** [#38476578740 — v1.2.7, four jobs successful](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/38476578740). The build log explicitly confirms native DLL import SHA `89d848b775fb67bc5e56043053f07f315eb414d46940eb7d343f9eaea3ab095b`; `NpcOutfitManagerSemanticContract.ps1` and `ScalarObservationReportContract.ps1` passed. Native revision label in manifest: `2.15.1-scalar`. Gameplay validation pending.

## How to publish a new native source revision (REQUIRED ORDER)

1. Edit native source; commit with tests/contracts. Let **Native CI** compile. Inspect **actual completed native success**, verify the DLL and SHA256 artifact.
2. In **one atomic change on the working branch**, update:
   - `MANIFEST.json.targetCET.profilerSha256` to the **new native artifact hash** (never copy an older run's hash);
   - `MANIFEST.json.profilerNativeVersion` to the chosen accurate native label;
   - the **pinned native run ID** in `.github/workflows/build.yml` (and artifact name if changed);
   - **only when explicitly requested by the user or at a user-agreed milestone:** change `VERSION.txt`, `Directory.Build.props` and `MANIFEST.json.packageVersion` together. Otherwise **retain the exact current product version** through further CI runs;
   - product notes and short [CORE.md](CORE.md) snapshot.
3. A commit triggers **Build G CET Runtime Profiler**, not the native workflow. Verify native import/hash check early; if red fix/re-run. Then verify full app, Resolver, contract tests and artifact upload.
4. Confirm **both** main artifacts exist. Native workflow success alone is not package success. Never direct a user to the native run for the combined application.
5. Note what CI checked and separately what is **not gameplay-verified**. Record final build IDs and SHA in [ACTIVE-STATE.md](ACTIVE-STATE.md), and update current values in CORE.

## Version versus CI run — locked release policy

- **The product version is NOT the build number.** An ordinary CI run or fixing a failing run does not cause `1.2.7 → 1.2.8`.
- Bump product version **only** on the user's explicit request or a significant milestone **agreed with the user**. That milestone need not correspond to one commit/run; multiple builds may share a version.
- Product version is shared by the Profiler and Resolver. On an authorized increment, synchronize `VERSION.txt`, `Directory.Build.props`, and `MANIFEST.json.packageVersion` in one commit. Otherwise leave all three unchanged.
- Update native DLL SHA-256, pinned native CI run, and accurate native revision information whenever needed to ensure a correct packaged DLL **without interpreting those changes as product release bumps**.
- Artifacts of repeated CI runs can have the same version name; identify the precise build by **Actions run ID / commit SHA / native DLL SHA-256**.
- Routine CI should not silently create, overwrite, or retag an official GitHub release. Release publication needs deliberate authorization and matching version metadata.

## Common previous mistakes — explicit preventions

- **Wrong division of work (historical mistake):** building a complete Profiler package in native CI. This is prohibited. Native CI only publishes the DLL; main build owns both applications and packaging.
- **Downloaded artifact layout:** GitHub `upload-artifact` of `portable/` extracts directly into product folders; it is **not** necessarily a nested `vX.Y.Z.zip`. Inspect the actual archive layout.
- **Mismatched native hash:** successive DLL compilations can have different bytes. Verify against the actual successful **referenced run**, not the first earlier attempt. Example: early run #38472029196 SHA `239e...` was **not** the artifact selected for v1.2.7; successful native run #38473605694 SHA begins `89d848...`.
- **Stale tracked binary:** the repo's `payload/` can still hold an older binary; the verified main build import is authoritative for its **published ZIP**. Do not misread this as source-controlled DLL replacement.
- **Premature “done”:** new workflow triggered ≠ successful build ≠ both artifacts uploaded. Follow run to a terminal conclusion. Managed CI also ≠ in-game native validation.
- **Tested rule ≠ deployed mod:** source-pattern NPC Outfit Manager rule is included in Resolver; its actual per-NPC game behavior, GC savings, pending applications and save/load remain separate in-game gates.

## Current files and commands

- Native source: `native-profiler-build/v2.11.0/Build-Profiler.ps1`, `CETRuntimeProfiler.h`, `Patch-CET-v1.37.1.ps1`.
- Native CI: `.github/workflows/native-profiler-rebuild.yml`.
- Main CI: `.github/workflows/build.yml`.
- Package manifest and hashes: `MANIFEST.json`; coordinated version files: `VERSION.txt` and `Directory.Build.props`.
- Managed app/Resolver: `src/G.CETProfiler.App/`, `src/G.CETResolver.App/` and `src/G.CETProfiler.Core/`.
- CI contracts: `tests/NpcOutfitManagerSemanticContract.ps1`, `tests/ScalarObservationReportContract.ps1` plus other `tests/*.ps1`.

Inspect both GitHub workflow definitions and the latest Actions before changing the pipeline. Update this file whenever responsibilities, artifact names or native-pinning policy changes.
