# G-Mod Collection semantic evidence set

This is a development/reference ledger, not a resolver payload.

The collection contained many CET mods, but only mods with a measured callback at or above the
3.0 ms/s material threshold and a semantic optimization shape belong in the semantic base catalog.
A mod being present in the collection is never enough.

## Semantic-library members from the measured collection

| Rule ID | Live owner | Peak measured ms/s | Semantic class |
|---|---|---:|---|
| gta-joyride | GTA - JoyRide | 120.083 | active 10 Hz / fully-idle 0.2 Hz + frame clock |
| repeatable-cyberpsychos | repeatable_cyberpsychos | 110.726 | multi-rate dormant |
| cybertrials | CyberTrials | 42.899 | realtime + fast/slow discovery + owned-mappin prefilter |
| streetgamespoker | StreetGamesPoker | 23.071 | active frame + 10/5/1 Hz distance/activity lanes |
| immersive-third-person | immersive_third_person | 19.887 | realtime camera + 10 Hz active / 2 Hz idle supervisor |
| nativeinteractions | nativeInteractions | 16.539 | active frame + dormant world/manager lanes + owned mappins |
| nightcitybilliards | NightCityBilliards | 14.250 | active frame + idle 5 Hz |
| immersivefirstperson | ImmersiveFirstPerson | 11.236 | frame-rate only while feature/session is active |
| minimap-widgets | Minimap Widgets | 8.226 | fast 5 Hz / slow 1 Hz + stealth-mappin prefilters |
| sitanywhere | sitAnywhere | 7.222 | hard idle gate, realtime workspot/scanner state |
| repeatable-increased-criminal-activity | repeatable_increased_criminal_activity | 6.566 | 4/1/0.2/0.1 Hz multi-rate lanes |
| givemeeverything | GiveMeEverything | 5.604 | global mappin ownership prefilter |
| roulette | Gambling System - Roulette | 5.261 | active frame / idle 10 Hz + prompt-mappin prefilter |
| driveaerialvehicle | DriveAerialVehicle | 4.671 | active vehicle realtime, normal/idle reduced work |
| illegal-mechanic | illegal_mechanic_phase1a | 4.225 | realtime pulse + dormant 5 Hz interaction lane |
| blackjack | gambling-system-blackjack | 3.646 | active frame / idle 20 Hz + 10/4 Hz sublanes + prefilter |
| questrunner | QuestRunner | 3.474 | active frame / idle manager 5 Hz / spawner feed 4 Hz |

Five of these also appeared independently in the G-CET motherload evidence set:
GTA - JoyRide, repeatable_cyberpsychos, CyberTrials, StreetGamesPoker, and NightCityBilliards.

## Measured collection mods deliberately NOT admitted to the semantic base

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
