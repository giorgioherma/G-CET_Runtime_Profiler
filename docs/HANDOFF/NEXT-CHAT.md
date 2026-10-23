# New-chat continuation prompt

Copy this to continue development. The active task and its outstanding steps are in [LATEST-WORK.md](../LATEST-WORK.md); supply additional captures or instructions as needed. No frozen version or previous CI identifiers.

> Continue `giorgioherma/G-CET_Runtime_Profiler` on `dev/resolver-profiler-handoff`. Read `docs/HANDOFF/CORE.md` then `docs/LATEST-WORK.md` (active task, not a historical snapshot), `docs/HANDOFF/BUILD-PIPELINE.md` and `docs/README.md` first. If the current task is Phase 1+2 optimization preservation, also read its single detailed brief at `docs/resolver/PHASE12-PRESERVATION-RECOVERY.md` before changing any generator. Then consult current product status, relevant subsystem documents and recent engineering history as needed. Verify live branch HEAD, manifest/version, both GitHub workflows and Actions before editing.
>
> Native CI builds **only** the native DLL/checksum; main CI packages **both** Profiler and Resolver using a pinned, hash-verified native artifact. Keep the shared product version until I explicitly request a bump or we agree on a significant milestone. Don't run CI for documentation-only changes.
>
> Profiler measures real callback, deep, burst, allocation and GC evidence. Resolver applies version-fluid, composable, source-proven generic then semantic changes to staged files and produces a ZIP, never rewriting my live mods. Preserve gameplay, lifecycle, idempotence and safe fallbacks. Unknown allocation ownership is not to be guessed. CI green does not prove game stability.
>
> **Task:** Continue `docs/LATEST-WORK.md` until its completion criteria are satisfied, updating that same file after material decisions/implementation/tests. When the current task is complete, record lasting outcomes in the subsystem docs/history; replace `docs/LATEST-WORK.md` with the next agreed task without renaming the file. Treat my new instructions as updates or an explicit redirection, not as silent completion.

[Handoff README](README.md) explains where to find detailed technical docs.
