# G-Mod Collection semantic evidence set

This is a development/reference ledger, not a resolver payload.

The collection contained many CET mods, but the semantic base is intentionally much smaller.
A measured callback at or above 3.0 ms/s only earns review. Admission additionally requires a
meaningful inactive state, expensive work that is genuinely unnecessary while inactive, and a
complete safe wake path. A mod being present in the collection is never enough.

## Semantic-library members from the measured collection

These are the collection mods that passed both filters: material runtime cost **and** a sufficiently clear inactive/wake model for semantic optimization.

| Rule ID | Live owner | Peak measured ms/s | Semantic class |
|---|---|---:|---|
| gta-joyride | GTA - JoyRide | 120.083 | active 10 Hz / fully-idle 0.2 Hz + frame clock |
| repeatable-cyberpsychos | repeatable_cyberpsychos | 110.726 | multi-rate dormant |
| cybertrials | CyberTrials | 42.899 | realtime + fast/slow discovery + owned-mappin prefilter |
| streetgamespoker | StreetGamesPoker | 23.071 | active frame + 10/5/1 Hz distance/activity lanes |
| nightcitybilliards | NightCityBilliards | 14.250 | active frame + idle 5 Hz |
| givemeeverything | GiveMeEverything | 5.604 | global mappin ownership prefilter |
| roulette | Gambling System - Roulette | 5.261 | active frame / idle 10 Hz + prompt-mappin prefilter |
| driveaerialvehicle | DriveAerialVehicle | 4.671 | active vehicle realtime, normal/idle reduced work |
| illegal-mechanic | illegal_mechanic_phase1a | 4.225 | realtime pulse + dormant 5 Hz interaction lane |
| blackjack | gambling-system-blackjack | 3.646 | active frame / idle 20 Hz + 10/4 Hz sublanes + prefilter |
| questrunner | QuestRunner | 3.474 | active frame / idle manager 5 Hz / spawner feed 4 Hz |

Five of these also appeared independently in the G-CET motherload evidence set:
GTA - JoyRide, repeatable_cyberpsychos, CyberTrials, StreetGamesPoker, and NightCityBilliards.

## Measured collection mods deliberately NOT admitted to the semantic base

### Hot but realtime / discovery-sensitive

- immersive_third_person — camera behavior is fundamentally realtime; reducing supervisor/maintenance rates is hand-tuning, not a strong dormant semantic case.
- ImmersiveFirstPerson — camera/height work is realtime whenever enabled; sleeping only disabled/unloaded edge states is too little gain for a version-sensitive rule.
- nativeInteractions — interaction framework must continuously discover proximity/state/input transitions; keep semantic cadence control out of the framework.
- Minimap Widgets — continuous HUD/presentation work with reasonable different refresh rates; not a true dormant feature.
- sitAnywhere — conceptually inactive most of the time, but safe dormancy requires a complete wake/discovery proof. Keep as research evidence until that is proven.
- repeatable_increased_criminal_activity — measured and behavior-classified, but it remains profile/research evidence only; no production AUTO injector is authorized.

The same admission policy excludes continuous world-discovery classes such as loot-marker systems and AutoLoot-style mods unless a future capture/source proof demonstrates a complete cheap wake path. Hot alone is not sufficient.

### Generic / low-value instead of semantic

- marmurbank — generic OnAction routing/prefilter problem.
- DriveBus — generic OnAction routing.
- freefly — generic OnAction routing.
- QuestTrackingToggle — generic OnAction routing.
- Dedrapanamgoondate — 3.16 ms/s onDraw only; already visibility-gated and not a current semantic scheduling target.

The resolver must therefore never treat "installed in this collection" as an optimization rule.

## Delivery rule

The semantic base catalog contains knowledge only.

A rule can generate output only when all of the following are true:

1. the current capture measured the callback at >= 3.0 ms/s;
2. the captured owner matches a catalog rule;
3. that mod folder exists in the user's current live CET mods directory;
4. the current live source graph proves the required semantic anchors;
5. an injector for that rule is enabled;
6. the injector rewrites only files that already exist in the live/staged mod.

The development reference optimization ZIP is behavior evidence only. Its files are never copied into
a generated pass and are not packaged with the standalone resolver.
