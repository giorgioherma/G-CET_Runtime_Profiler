# Semantic Resolver mod-pass source index

The Resolver keeps one authoritative source implementation per mod in `ModPasses/<Mod>.cs`. Multiple distinct catalog rules for the same mod share that file, but each retains its original rule ID and admission/postcondition contract so previously recorded captures remain compatible.

The selector in `SemanticInjectors.cs` remains the only dispatch point; generic AUTO, complete-stage proofs, marker handling and one-pass pristine-source behavior are not changed by this file-only refactor. Shared source-safe helpers stay there. Fixed runtime and external dedicated injectors remain separately owned.

**Development rule:** repair the mod's authoritative implementation, not an appended patch-over-patch rule. Validate pristine source with one generation pass, upgrade/idempotence of older staged source, format variants, fail-closed behavior and existing regression suites before delivery.

| Mod source file | Injectors |
|---|---|
| `AdvancedSettings.cs` | 1 |
| `AerialRaceActiveIdleSplit.cs` | 1 |
| `AirBackFlip.cs` | 1 |
| `AlternativeMidairMovement.cs` | 1 |
| `AppearanceMenuModSqueeze.cs` | 1 |
| `AutoAmmoCrafting.cs` | 1 |
| `AutoDropWeaponOnPickupEquip.cs` | 1 |
| `AutoLoot.cs` | 3 |
| `BackStepDuo.cs` | 1 |
| `BetterLootMarkers.cs` | 1 |
| `Blackjack.cs` | 1 |
| `CallList.cs` | 1 |
| `Combat.cs` | 1 |
| `CyberTrials.cs` | 1 |
| `DedkaAutoShop.cs` | 1 |
| `DedraPalmjetQuickslot.cs` | 1 |
| `DedraThirdPersonCombatAddon.cs` | 1 |
| `DiscardAmmoOnReload.cs` | 1 |
| `DriveAerialVehicle.cs` | 1 |
| `DriveBus.cs` | 1 |
| `DroneCompanionsRevamp.cs` | 1 |
| `DynamicOutfitsJudy.cs` | 1 |
| `EasyTrainer.cs` | 2 |
| `FenixMantisBlade.cs` | 1 |
| `FovSentinel.cs` | 1 |
| `GameEntityExaminer.cs` | 1 |
| `GhostVoidSystem.cs` | 1 |
| `GigCore.cs` | 1 |
| `GiveCraftMat.cs` | 1 |
| `GiveMeEverything.cs` | 1 |
| `Hotscenes.cs` | 1 |
| `GoodFeelings.cs` | 2 |
| `IllegalMechanic.cs` | 1 |
| `ImmersiveFirstPerson.cs` | 1 |
| `ImmersiveHeadInertia.cs` | 1 |
| `ImmersiveThirdPerson.cs` | 1 |
| `JoyRide.cs` | 1 |
| `JoytoysOfNightCity.cs` | 1 |
| `MarmurBank.cs` | 1 |
| `MetroSystem.cs` | 1 |
| `MinimapWidgets.cs` | 2 |
| `NativeInteractions.cs` | 1 |
| `NightCityAlliesMissions.cs` | 1 |
| `NightCityBilliards.cs` | 1 |
| `NightCityPizza.cs` | 1 |
| `NpcdHotline.cs` | 1 |
| `OverclockedJenkinsTendons.cs` | 1 |
| `QuestRunner.cs` | 1 |
| `QuestTrackingToggle.cs` | 1 |
| `RepeatableCyberpsychos.cs` | 1 |
| `RepeatableIncreasedCriminalActivity.cs` | 1 |
| `Roulette.cs` | 1 |
| `Shift.cs` | 1 |
| `SimpleNotepadCet.cs` | 1 |
| `SitAnywhere.cs` | 1 |
| `SongsDeck.cs` | 1 |
| `SpecterRecoveryPrerequisites.cs` | 1 |
| `StraightEdgedControls.cs` | 1 |
| `TeleportGatewaySystem.cs` | 1 |
| `TunnelRescue.cs` | 1 |
| `YaibaDistantBathtub.cs` | 1 |
