# G-CET + G-RED — unified two-page application (target product contract)

**Status:** accepted target UX/architecture, not implemented in the current two-app release. **Decision date:** 2026-10-16. Keep current development, CI, profiler native and Resolver stable until this workflow passes end-to-end tests. The present static-location RED4ext experiment stays isolated but must be designed for later integration.

## One application, one game-root selection

The packaged application contains **both CET and REDscript profiling capabilities and the Resolver**. There is no separate Resolver launcher, no extra mod-path field, no separate static-location service UI, and no requirement for the player to understand native build artifacts. The UI has **two pages, three primary actions**:

1. **Page 1 — Profiler:** `Install Profilers`; `Create Game Capture`; `Continue to Analytics & Pass Generation` (disabled until a complete capture is verified).
2. **Page 2 — Analytics & Pass Generation:** automatic evidence collection, Resolver analysis and concise optimization report; `Generate Optimization ZIP` after analysis is complete.

The only ordinary path input is the **Cyberpunk 2077 game root**, selected once. Derive the source of currently installed CET mods from exactly
`<gameRoot>/bin/x64/plugins/cyber_engine_tweaks/mods`.
Inspect REDscript sources under the current effective game installation and any supported mod-manager overlay. Previously uploaded `modsLIVEnow` archives and original mod packs are regression fixtures, **not** the production source of installed mods. The actual game-root selection defines membership. Never silently include absent future-stack mods or mod names from a historical rule catalog.

### Page 1: install and capture

- Preflight: verify the game root and supported runtime installation; detect **0-Engine** and **G-RED Runtime Framework**, version/API and source-proven integrity/compatibility where available. Different missing dependencies must produce distinct actionable status; do not treat existence of a directory as sufficient. Existing CET and RED4ext requirements are checked. Do not overwrite/automatically download either framework without an explicit separate action; no disguised dependency installation. Fail safely before producing an incompatible PASS.
- `Install Profilers` performs one **transactional, reversible** session for both CET native and REDscript native components and any temporary controls. Hash and record original files and session identity, recover from partial installs/failed startup, never mix a native DLL with a mismatched PDB. Game-install files are preserved. The native profiler is separate from the static-location game-runtime DLL.
- `Create Game Capture` starts the capture-ready workflow and launches CapFrameX when configured/installed. Provide a short instruction: **around five minutes**, sample idle, running, jumping, driving, combat; **avoid all menus, vendor/shop screens and other menu workloads** for representative gameplay. Five minutes is guidance, **not** an auto-stop timer. Clearly identify CapFrameX missing/unavailable without falsely claiming frametime data; preserve ability to collect the profiler's own evidence when supported.
- The continuation button stays disabled until verified capture readiness: the expected capture session reached a valid final/waiting state and expected evidence files are complete. A fixed elapsed duration or stale capture directory must **not** unlock it. If the game/capture crashes or evidence is partial, offer clear retry/recovery state.
- The continuation click automatically verifies and **restores both original native binaries** and uninstalls temporary controls; collects and validates raw CET + RED + optional CapFrameX evidence, then moves to Page 2. **Restoration failure blocks normal continuation** and exposes explicit recovery, rather than silently continuing with instrumentation in the game. Preserve generated evidence outside the game as diagnostic/report data.

### Page 2: analyze and package

- On arrival, start the Resolver automatically; no extra `Analyze` button. Use collected real captures together with **read-only inspection of installed, effective mod sources**. Derive generic and source-proven semantic optimizations, including eligible native static-location/extended-dormancy substitutions.
- Display a compact report: detected CET/RED mods, optimizations accepted, already satisfied, skipped/unsupported and the reasons that matter; native service workload cost vs retired mod CPU/allocation/native boundary work when actually measured. Do not invent performance gains. Advanced per-mod diagnostics can be expanded/exported separately, **not packed into the game-install ZIP**.
- Enable `Generate Optimization ZIP` only after source proof, dependencies, compose/idempotence contracts and packaging validation succeed; when none qualify, explain why rather than generating a misleading artifact.
- Produce **one self-contained game-root-relative ZIP**, with only files that install into the game. Examples: `bin/x64/plugins/cyber_engine_tweaks/mods/<owner>/...`, `r6/scripts/...`, `red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll`, `red4ext/plugins/G-CET-StaticLocations/G-CET.locations`; include other game-root paths only when genuinely required by a proven transform. ZIP must **not** contain a wrapping directory, instructions/readmes/docs, profiler controls/binaries/PDBs, captured data, or internal analysis JSON. Keep hashes, validation logs, test fixtures and human reports in the application workspace.
- Include the location runtime and compiled installed-only manifest **only if** the Resolver has accepted source-proven wake substitutions. No speculative static anchors, no duplicate Lua-owned location cache, no external manually maintained owner lists. Generated source must remove obsolete location polling/position reads when the native wake fully replaces them; retain all independent events, quest progression, cleanup, first interaction, and fail-open fallback.
- The user decides whether to apply the ZIP; ZIP generation does **not** mutate live mod source. The app must not claim that the ZIP is gameplay-validated without in-game tests.

## Native location ownership and future G-RED integration

- During development use the independently built RED4ext static-location DLL on `dev/native-static-anchors-experimental`; its C++ index, per-owner anchors, subscriber streams and native diagnostics are shared infrastructure. The eventual integrated Resolver can package it as `red4ext/` alongside `bin/` and `r6/`; this is the **default packaging decision** until G-RED's native loading/ABI, upgrade and ownership compatibility are proven.
- Integrating the DLL into the **G-RED Runtime Framework distribution** is an optional future deployment refactor, **not an assumption** that a REDscript-only package can load arbitrary DLLs. Maintain one native owner, one ABI/API version, no duplicate static-location DLL and no dependence on native Profiler capture to run optimized mods.
- The Profiler will eventually collect native static-location counters at bounded cadence, with accurate per-consumer lost/resync counts and measured native service overhead. Analyze saved native-to-Lua calls, actual retired author scheduler jobs, allocations and frametime before assigning ROI. Never call an early Lua return a native dispatch elimination.

## MO2 compatibility: explicitly unresolved

The chosen game directory is the game's installation root, but **Mod Organizer 2 may present a different effective mod tree at runtime through its virtual filesystem**. Do not merely scan physical `bin/.../mods` and assume the visible MO2 profile matches it. Design a future `source provider` abstraction:

- Direct-install provider: game-root filesystem, current behavior.
- MO2 provider: selected profile, ordered/effective VFS file precedence, source provenance, and generated-overlay deployment target, with read/patch equivalence and conflict proof.

Do not silently read/write unselected profiles. Until verified, show MO2 compatibility as unsupported/pending and avoid issuing a pass that targets the wrong mods. This is an implementation dependency, not a reason to complicate the visible two-page interface prematurely.

## Internal state machine (no third UI page)

`GAME_SELECTED -> PREFLIGHT_OK -> PROFILERS_INSTALLED -> CAPTURE_ACTIVE -> CAPTURE_READY -> RESTORING -> EVIDENCE_COLLECTED -> ANALYZING -> REPORT_READY -> ZIP_READY`

Each transition is evidence-backed and resumable or recoverable after interruption. The important gate is `CAPTURE_READY` based on the **current** capture session, not just the presence of a past CSV/ZIP. Any failure to restore both native layers switches to `RECOVERY_REQUIRED` and does not silently continue. Dependency/source-analysis failures yield an honest `NO_PASS` outcome.

## Release compatibility and per-mod opt-out (user decision)

**The optimization tool is best-effort software for heterogeneous mod installations, not a guarantee of gameplay correctness in every possible stack.** No release criterion requires a developer to manually play through every supported mod, quest or combination. There will be unsupported versions and inevitably some missed gameplay interactions. The user-facing release notes and application report must state the limitation clearly and avoid absolute claims of zero bugs or universal compatibility.

- Preserve meaningful automated source/semantic validation, bounded runtime checks, original/partial/idempotent tests and honest fail-open decisions; these materially reduce avoidable breakage but are **not** a substitute for exhaustive in-game compatibility testing and do not pretend to be.
- **Per-mod isolation is an explicit packaging invariant.** Where the optimization rewrites a CET owner's Lua, emit its changed source only under `bin/x64/plugins/cyber_engine_tweaks/mods/<actualModFolder>/...`. Do not combine unrelated owners' changed scripts into one mod folder, overwrite their originals when merely generating the ZIP, or make other owners depend on a given mod's transformed code. Then **before application**, a player can simply remove the offending mod's directory from the ZIP and install the remaining optimizations.
- REDscript/source adapters and shared native runtime can affect paths outside CET's owner folder. Every emitted game-relative file must carry **internal** owner attribution/dependency metadata in the application's workspace, not in the game-only ZIP. Prefer naturally separate owner-specific REDscript files and reversible modules. If an owner has coupled files or generated shared data, omitting a CET folder alone may be insufficient: support **exclude that owner and regenerate the same optimization ZIP** (an advanced report action, not a fourth primary button). Filter shared anchor manifest, consumers and dependencies to accepted owners; a shared DLL is retained only when other admitted owners use it.
- **After installation**, removing a mod directory from the old ZIP does **not** undo a previously installed optimized file. Player must restore that mod's original backed-up files, reinstall that mod's original version, or apply a verified owner-scoped rollback/clean regeneration. Documentation must not claim otherwise. Avoid deleting the original installed mod wholesale or overwriting other mods' changes.
- Recovery defaults to **isolate the reported offender and retain the rest of the pass**. Capture optional crash/source diagnostics and repro evidence only if the player chooses; do not demand five-minute captures and exhaustive test matrices for every bug report. Do not disable all optimizations just because one adapted mod fails.
- The generated game-root ZIP remains clean: only deployable game files. Ownership map, hashes, original-file restoration plan and exclusions live in the app's workspace and are optionally exportable separately. The simple two-page/three-action primary UI does not become a troubleshooting wizard.

### Installation policy: Vortex recommended, backup before applying anything

- **Recommend Vortex** to install the complete generated optimization ZIP as a **separately managed mod**, preserving the ability to manage conflict priorities and disable the generated optimization without dismantling the underlying mods. This is a recommendation, not a required dependency or a claim that Vortex can reverse arbitrary manual changes. Its actual rollback behavior must be verified; do not mix Vortex deployment and manual overlays to the same files without understanding precedence.
- **If installing manually, BACK UP BEFORE APPLYING ANYTHING.** At minimum back up the **entire current CET mods directory** `<gameRoot>/bin/x64/plugins/cyber_engine_tweaks/mods` and the **entire current REDscript scripts directory** `<gameRoot>/r6/scripts`, preserving their structure and content outside the game installation. This must be done before applying **any generated optimization**, not merely before copying an individual file. If the generated ZIP includes native RED4ext payloads, also back up the affected `<gameRoot>/red4ext/plugins` contents.
- Place the warning **next to the ZIP generation/apply instructions inside the app or on the distribution page**, never as a readme inside the game-root-only optimization ZIP. User-facing wording: **"Vortex installation is recommended. If installing manually, BACK UP your CET and REDscript mod folders BEFORE APPLYING ANY OPTIMIZATIONS. Also back up affected RED4ext files when included."**
- A generated ZIP must not directly modify the installed game; manual backup and rollback responsibilities remain clear. Disabling a generated Vortex mod may recover managed files, whereas manually overwritten original files must be restored from the user's backup. A failing owner can be excluded and the ZIP regenerated without discarding independent optimizations.

## Acceptance before calling the unified application complete

1. Single input game root drives CET mod analysis and REDscript source discovery; no hardcoded snapshot paths and no separate mod-folder question.
2. Both profilers install/restore atomically, including interrupted session, crash, mismatch, absent game, disabled extension and missing native dependencies.
3. The continuation button stays disabled for an incomplete/stale/wrong capture, is enabled only for a verified finished capture, and restores temporary game files before entering normal analytics.
4. Optional CapFrameX launch and retrieval are explicit; no phantom frame timings when unavailable.
5. Automatic analysis produces a meaningful report, performs a source-proven one-pass composition of installed mods, and does not require the player to understand ModPass rules or edit YAML.
6. Generated ZIP contains **only game-relative runtime files**, has correct root topology, and has no docs, controls, tool binaries, PDBs, diagnostics or stale mods.
7. Existing Profiler/Resolver continue shipping unchanged until coordinated integration is authorized. Product version changes require a separate explicit release decision.
