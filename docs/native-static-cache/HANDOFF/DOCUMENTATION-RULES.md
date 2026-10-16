# G-LookupCache — documentation rules

## Four distinct layers (avoid endless logs)

1. **HANDOFF/**: short, durable, reusable project identity, architecture invariants, code locations, CI/use workflow and the new-chat prompt. No per-run SHA dumps, growing capture notes or transient TODOs in CORE.
2. **LATEST-WORK.md**: **single fluid current direct task**, ideally one screen (~0.5–1.5k words maximum, usually much less). Update in place as work proceeds. It must say: current goal, what is done and verified (current SHA/CI), what specifically remains, blocker/owner, exact next step, done definition. No append-only timeline, no prior finished task list, no generic roadmap. **When the task is finished, move evidence to history/details and replace the task section with the newly agreed task**, preserving the same filename. If no next task is agreed, say `AWAITING NEXT TASK`.
3. **details/**: durable technical explanation, API and invariants, integration proof requirements, test matrices and reasoning. Organize by subsystem/subject; modify old detail docs rather than pile up contradictory "latest" statements.
4. **history/**: completed milestones, decisions, failed experiments, measured observations and reproductions. Date, branch, commit SHA, CI link, evidence caveats. Chronological history does **not** belong in LATEST-WORK.

## Required behavior of a new chat

- **Read before editing**: `HANDOFF/CORE.md`, `LATEST-WORK.md`, `HANDOFF/WORKFLOW.md`, then relevant details/source. Retrieve the *current* branch HEAD and real CI.
- At start, reconcile differences between source, last documented state and user instructions; fix stale docs rather than trusting them over code. Do not silently overwrite another agent's work; use expected HEAD checks.
- **After each material decision or implementation**, update the current task's short progress and any lasting contract. Do not create new `LATEST-WORK-v2` files, date-stamped competing handoffs or duplicate changelogs.
- On verified completion, **move implementation proof, current CI URL and outstanding non-task risks to details/history**, mark completion there, and **replace** the contents of `LATEST-WORK.md` with only the next agreed active task (or `AWAITING NEXT TASK`).
- Each doc must distinguish **implemented/CI-tested**, **source-reviewed**, **gameplay-tested**, and **proposed**. Avoid saying `complete` or `safe to install` based solely on green Windows/Linux CI.
- Update cross-links/index when architecture changes. Keep docs small and targeted; no raw 100k-line profiler transcripts or 150k-character running entries in the active task.
- **Documentation-only commits are legitimate** and should not trigger an unnecessary native/product build. Do not bump product/SDK version or modify production profiler and Resolver simply for documentation.
- The internal Resolver decision-layer codename belongs only in internal developer files; never surface it in end-user UX, generated ZIPs or release marketing.

## Finishing a chat

Before closing: verify HEAD and the matching CI outcome, edit/commit LATEST-WORK if the task changed, capture durable results elsewhere, state exactly the dependency on the user and whether the ZIP is a native smoke build, a source experiment or a validated Resolver-generated optimization.
