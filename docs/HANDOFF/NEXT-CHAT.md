# New-chat continuation prompt

Copy this and supply the task or capture. No frozen version or previous CI identifiers.

> Continue `giorgioherma/G-CET_Runtime_Profiler` on `dev/resolver-profiler-handoff`. Read `docs/HANDOFF/CORE.md`, `docs/HANDOFF/BUILD-PIPELINE.md` and `docs/README.md` first. Then consult current product status, relevant subsystem documents and recent engineering history as needed. Verify live branch HEAD, manifest/version, both GitHub workflows and Actions before editing.
>
> Native CI builds **only** the native DLL/checksum; main CI packages **both** Profiler and Resolver using a pinned, hash-verified native artifact. Keep the shared product version until I explicitly request a bump or we agree on a significant milestone. Don't run CI for documentation-only changes.
>
> Profiler measures real callback, deep, burst, allocation and GC evidence. Resolver applies version-fluid, composable, source-proven generic then semantic changes to staged files and produces a ZIP, never rewriting my live mods. Preserve gameplay, lifecycle, idempotence and safe fallbacks. Unknown allocation ownership is not to be guessed. CI green does not prove game stability.
>
> **Task:** [describe task here].

[Handoff README](README.md) explains where to find detailed technical docs.
