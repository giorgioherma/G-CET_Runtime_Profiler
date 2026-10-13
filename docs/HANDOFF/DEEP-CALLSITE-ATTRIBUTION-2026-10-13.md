# Existing native 2.15 deep-callsite attribution — verified join (2026-10-13)

Joined the supplied `CONTAIN_THE_DATA` archive's **59,959 Deep_Callsites** and **8,314 Deep_Functions** rows on (`RegistrationId`, `ProfileEpoch`, `ChildFunctionKey = FunctionKey`). **59,959/59,959** callsite rows find scoped function rows. **Important:** `Deep_Registrations` has changed `ProfileEpoch` values for many registration IDs, so use stable `RegistrationId` to resolve the registered mod name; registration+epoch is necessary for *function/callsite matching*, but not for mod-name lookup. Confirm uniqueness on registration ID before publishing.

Ranked **2,809 unique (mod, caller-source, line, child-name) tuples** based on observed sampled child calls. This is a bounded deep sample, not runtime-total counts or observed stable-result reuse. Examples:
- **NPC Outfit Manager:** `init.lua:531` `tostring` **1,196** sampled child calls; `init.lua:536` `gsub` **1,140**; `init.lua:1826` `GetNPCID` **598**; `init.lua:531` `GetRecordID` **598**. This strongly corroborates recurring identity string building in the prior source audit. Sampled nested inclusive time includes pauses/descendants; do not interpret as net savings.
- **AutoLoot** `modules/logic.lua:1502` `IsA` **857** sampled child calls; inspect source guard semantics before changing.
- **ImmersiveFirstPerson** `Modules/Helpers.lua:145` `GetInt` **648** sampled calls, plus native `__index` dispatch. Preserve active camera responsiveness.
- **0-Engine** `modules/Scheduler.lua:213/224` `jobBegin/jobEnd` **583** each; scheduler instrumentation/dispatch may be necessary overhead.
- **tunnel_rescue** `json/json.lua:102` `gsub` **757** sampled calls; prove whether this is cold startup/encoding or recurring producer before prioritizing.

**Critical attribution limit:** `Deep_Functions.GGCTracerSampledOwnAllocatedBytes/Allocations` are per (`registration`, `epoch`, `function`) totals. The same function can occur under several callsite rows. Never duplicate those totals for each caller or allocate them pro rata as established fact. Caller source/line prove *invocation edges* only; they do not prove which caller is responsible for function-wide allocation amount. Group sampled own allocs per function once, then list possible callsites with ambiguity/coverage. Owner 0 remains out of scope. No native 2.16 needed to extract this existing source evidence; native 2.15 stays pinned.

Local derived index: `CET_Deep_Callsites_Ranked_2026-10-13.csv` (2,809 rows). Recommending targeted source/result instrumentation and a function-to-callsite ambiguity report next. No code or mod changes.
