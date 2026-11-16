# After a GHPC update

GHPC is a Unity Mono game with no obfuscation, so the mod patches its classes with Harmony and reads private members
through reflection. An update that renames or removes one of those breaks the feature that uses it and nothing else.
This page is how to find what broke and fix it.

## Start with the offline check

```
powershell -ExecutionPolicy Bypass -File tools\check-game.ps1 -GameDir "<the game's Bin folder>"
```

It builds the mod and looks every patch target and every `Reflect` member up in the new game's DLLs, without starting
the game. Anything gone shows as `MISSING` with the file it's used in. Names the code only knows at run time are in
`tools\check-game-list.txt`, and the script says `NOT COVERED` if the code gains one the list doesn't have. The
RepairKit patch isn't checked because it hooks another mod, not GHPC.

Then, in this order:

1. Build against the new `Assembly-CSharp.dll` and fix the compile errors. Those are public members and the
   compiler points at every one.
2. Fix whatever `check-game.ps1` lists.
3. Start the game and read `MelonLoader\Latest.log` from the top. Harmony errors and `Reflect` warnings come before the
   main menu.
4. Check the Leclerc is in Instant Action, the mission customiser and the American side's campaign.
5. Spawn one and read the log again. Conversion runs at spawn, so most warnings only show up here.
6. Go through the test list at the bottom.
7. Play a mission with the mod off. Some patches touch every unit, so check a vanilla T-80B and Leopard 2A3 still behave.

## What it looks like in the log

- `Game field X.y not found: a game update may have changed it, ...` comes from `Reflect`. The word after "Game" can
  also be `method`, `property` or `property setter`. Once per session per member. `can't be read as bool (...)` means
  the field is still there but its type changed. `can't be looked up (...)` usually means a method gained an overload.
- A Harmony error at startup naming a patch class, with `Undefined target method` or similar: the patch target went.
- `<feature> failed (the game carries on without it this time; 1 of 3 logged)` is `PatchGuard`. The patch ran and the
  code inside threw. The stack trace has the line. It goes quiet after three.
- `Unity exception in our code: ...` (or `error`) is `UnityErrors`, an exception in one of the mod's behaviours or
  coroutines.
- `Leclerc SXXI <step> failed (<what you get instead>): ...` is one row of the conversion table throwing. The rest of the
  spawn went on. `Leclerc SXXI <step>: <unit> was converted after it spawned` means the spawn patch missed that unit and
  the sweep caught it, which leaves out the steps that only work at spawn.
- `Leclerc SXXI modules: ...` and `Leclerc SXXI ammunition self-check ...` list what they couldn't find. The ammo one
  ends by asking for the log, ignore that bit.
- `donor prefab 'T80B' not in the unit registry` or `vanilla material '...' not found`: a borrowed part couldn't be
  copied.
- `Leclerc SXXI: no rig map; the hatches stay closed`, `no material roles; glass keeps the bundle's own materials`
  and `armour is incomplete; keeping the donor armour` aren't a game update. A text file has gone
  missing from `Resources\`, or didn't get embedded, and the bundle didn't have a copy either.

A missing reflected member is looked up once and cached, and the feature code checks for null and skips, so a rename
shows up as a feature that's gone quiet, not a crash. The one exception is `TraverseLimits`, which wraps
`AimablePlatform.enforceEulerLimits` in a `MethodDelegate` and loses its whole class if that method goes.

Things that break most often, roughly in order: a private field or method renamed, a patched method's parameters
changed, a public member renamed (compile error), a prefab or material renamed, and last, nothing renamed but the game's
behaviour around the mod changed. That last one gives no log line at all. You only see it by playing.

## Fixing a renamed member

Open the game's `Assembly-CSharp.dll` (`GHPC_Data\Managed`) in a .NET decompiler, find the class from the warning and
look for the same kind of member with a similar type and name. Find Usages on the old name's neighbours usually gives
it away. For a method, match the parameter types and read the body. Decompiling the previous game version and diffing
the class is the quickest way if you still have it. Change the string or signature, build, and test that feature.

If a whole mechanism has gone (say `LiveRound.penCheck` is split up), the code using it has to be rethought, not
renamed.

## If GHPC moves to a new Unity version

The bundle was built with Unity 2022.3.62f2, the version GHPC runs on, and a bundle has to match the game's Unity
version to load. If that changes, expect `Leclerc SXXI bundle could not be loaded: <path>` and no `Loaded Leclerc SXXI
visuals` line. The mod still runs but the Leclerc keeps the T-80B's model and armour. The fix is a new bundle built
against [bundle.md](bundle.md). The code doesn't change.

## Where the mod hooks in

Files are in `Common\` unless they say `LeclercSXXI\`. Each group ends with what you see in game when it breaks.

**Unit lists and spawning.** `UnitRegistry.cs` patches `MissionConfigMenuPanel.GetUnitKeysByClass`,
`UnitSpawner.GetMetadataByUniqueName` and `UnitSpawner.SpawnUnit`, clears the spawner's and customiser's name caches,
and sets `Unit._uniqueName` and `_friendlyName`. Broken: the Leclerc is missing from a list, spawns as a plain T-80B,
or shows as T-80B in the HUD. A unit the spawn patch misses is still caught by the sweep in `ConvertSweep.cs`.

**Ammunition and the menus.** `PortAmmo.Menu.cs` patches the customiser's ammo dropdown and the campaign's logistics
and unlocks, and `PortAmmo.Spawn.cs` the restock check. `LoadoutManager.RegisterAllBallistics` is called after the ammo
list is swapped. The round models are the Leopard 2's, re-textured and reshaped from `Resources\`. Broken: the customiser
shows the T-80B's rounds, rounds fly with no ballistics, or the Leopard 2's rounds turn up with their own paint.

**The EXPL's fuze.** `AirBurst.cs` patches `LiveRound.DoUpdateImpl` and reads the round's ranged fuse fields to burst it
in the air. Its patches on `FireControlSystem` and `BallisticComputerRepository.GetSuperelevation` lift the aim for the
overhead burst, and `FragmentCone.cs` sets the fragments' penetration again after `LiveRound.Init` resets it. The fuze
menu, like every vehicle menu entry here, hangs on `PlayerInput.AddSmokeButtons`. Broken: the EXPL goes off on impact
whatever the menu says, bursts with no effect drawn, or "over" aims at the target itself.

**Gun, reload and sound.** `GunSetup.cs` sets the gun's limits, drives and reload stages, copies the gun's codex entry
before renaming it (every T-80B shares it) and rescales `AmmoFeed`'s stages to the 6 s and 8 s cycles. `ReloadRules.cs`
holds the reload rules. `GunShotSound.cs` and `GunShotRules.cs` put the Leopard 2's shot in place of the game's, and
`ReloadSound.cs` plays the reload mix on the game's reload clock. Broken: the T-80B's reload time, every T-80B named
after the Leclerc's gun, the wrong shot sound, or a reload sound that ends before or after the gun is loaded.

**Crew voice.** `CrewVoices.cs` sets `CrewVoiceHandler._chassis` and `_reloadType` on the copied M1IP voice and points
`Unit._crewVoiceHandler` at it. Broken: Russian crew, or an American crew that doesn't react to driving.

**Gunner's sight, fire control and the HL 120.** `GunnerFcs.cs` patches `FireControlSystem` (`AdjustAimByRatio`,
`Update`, `FiringBlocked`, `UpdateLaserAim`, `DoLase`). Its `Resync` rewrites the fields the FCS caches in Awake and
DeferredStart. `ReticleMeshes.cs` clears `ReticleMesh.SMR` for every reticle swap. `ThermalSight.cs` patches
`PlayerInput.Update` and two `CameraManager` methods for the night toggle. The HL 120 head is a still prop, so the
commander's sight code in `Common` (`CommanderCupola.cs`, `TargetDesignation.cs`) is stamped here but nothing calls it.
Broken: the sight swings past its stops, the gun fires before it's laid, reticles don't draw or keep the old one, or the
night key does nothing with a dead day sight.

**Smoke.** `SmokeLaunchers.cs` rebuilds the grenade slots (8 grown to 14, one volley) and patches
`VehicleSmokeManager.FireSmokes` to rebuild the crew lists a runtime spawn never got, and `SmokeGrenadeDonor.cs` patches
it too, to register the Leopard 2's M82 again before every salvo. Both read a dozen `VehicleSmokeManager` and `Grenade`
fields. Broken: the smoke launchers never fire, fire in two salvos, or throw the T-80B's grenade.

**Reload, restock and the coax.** The tank's own patches in `LeclercSXXI\` all sit on the ammunition code. The conveyor
patches `AmmoCarousel.SeekSlotWithClipType` (so the nearest round of the asked type comes to the door) and
`AmmoFeed.FinishClipReload`. The restock from the drum patches `LoadoutManager.DoRestockTick`, `AmmoFeed.CanContinueReloading`
and `CrewBrainWeaponsModule.BusyRestocking`, so the transfer waits while the gun loads, and the loading dip patches
`AmmoFeed.Update`. The hand-load sound patches `AmmoFeed.FeedNewClip`, and the coax belts `AmmoFeed.TryFinalFeedNewClip`
and `AmmoFeed.ReserveCount`. `Conversion.Voices.cs` patches `CrewVoiceHandler.PlayVoiceLine` for the American loader callouts.
Broken: the conveyor turns the wrong way or not at all, the gun loads before the door opens, a transfer from the drum
runs while the gun is loading, the last coax belt reads wrong, or the crew calls a loader the tank hasn't got.

**The bustle.** `BlowoutRack.cs` patches `Compartment.DoUpdate`, `Compartment.DoBurst` and
`CompartmentExit.SetFireRatioByFlameHeight` and reads `FlammablesManager.DidBurst` and a few `AmmoRack` fields. The
conveyor, rammer and door are the mod's own behaviours in `LeclercSXXI\`, driven from the game's reload. Broken: the
bustle doesn't vent through its panels, a fire in it stops the engine, or the conveyor and the gun's reload go out of step.

**Armour and rounds.** `DmArmourApply.cs` patches `LiveRound.penCheck` and sets `UniformArmor._armorType` and friends and
`VariableArmor._armorType`. Broken: every piece takes the game's default armour material, which is the biggest effect of
anything on this page.

**Rig, modules and heat.** `Followers.cs`, `PivotMove.cs`, `TiltedRing.cs`, `TraverseLimits.cs` and `GearFollow.cs` use
`LateFollow` shifts and `AimablePlatform._originalLocalRotation`. `ModuleBuilder.cs` sets `DestructibleComponent` and
`UniformArmor` fields from the module list. `ThermalHeat.cs` writes `HeatSource.heat` and clears its renderer list.
`ExhaustMove.cs` moves the base's exhaust emitters and `Mobility.cs` writes the engine, gearbox and top speeds. Broken:
parts displaced or crooked, modules with the wrong health, parts black in white-hot, the exhaust over the rear deck, the
T-80B's speeds.

**Other mods.** `RepairKitCompat.cs` patches RepairKit's `Apply(Unit, NwhChassis)` if it's installed. A rename there
logs `RepairKit found, but not its repair method`. Nothing to do with GHPC updates.

## Names looked up as strings

These don't go through `Reflect`, so a rename gives no warning of that shape.

`tools\check-game.ps1` reads the literal names in `Reflect.*`, `AccessTools.Method`, `Traverse.Create(x).Field`,
`Type.GetField` / `GetMethod` and the hit-zone table of `ModuleBuilder`. It cannot check a field enumeration (`GetFields`
in `PortAmmo.MakeClip` and `DonorParts.Scrub`) or a lookup whose name is a variable: after a game update read those
against the class by hand. The script lists where they are.

The rest, which no script reads:

- **T-80B rig** (`BaseRigT80B.cs`): the hull, turret and gun bones, the renderers it hides and the running gear it hides.
  A name that goes shows as the T-80B's hull or wheels left drawn inside the Leclerc.
- **The T-80B's armour and interior** that War Thunder's model replaces (`vanilla_edits` in `Resources\`, plus
  the collider paths in `Resources\interior_map.txt`). A renamed one stays switched on inside the Leclerc's armour, and
  the armour warning names it. Some carry GHPC's own trailing space. Keep it.
- **Donors**: units `T80B`, `M60A1` (the army), the Leopard 2 and Leopard 2A3 (rounds, shot, grenade, engine and heat), the
  M1IP (voices, the coax and the thermal's scope look) and the units `material_roles` names. `DonorParts.cs` copies them
  at the main menu, because VanillaAssetUtil can destroy the prefabs after a mission.
- **Ammunition** (`LeclercSXXI\Firepower.cs`): the DM23 and DM12A1 clips of the Leopard 2 and the T-80B's HE clip.
- **Fonts and materials**: `Soviet7SegmentFont SDF` and `LiberationSans SDF` (named in the reticle files), `Standard (FLIR)`,
  and `see-through teal gray` for the x-ray (no log line if that one goes).
- **Scenes and input**: the menu scenes in `MenuScenes.cs`, `TR01_showcase`, and the buttons `Toggle Night Sight`
  and `Smoke Grenades`. A renamed button silently does nothing.

## Test list after a repair

1. Start the game and read the log from the top: no `Game field`, no Harmony error.
2. Leclerc in Instant Action and the customiser. Spawn one and read the log.
3. Drive, traverse, elevate. Lase and fire. A reload of the same round (6 s) and of another type (8 s), with the sound
   ending when the gun is loaded.
4. Both thermal fields and the day channel, then again with the HL 130 shot out and the secours view.
5. The commander's free-look view over the roof, clear of the HL 120 head.
6. An EXPL on each fuze setting, lased first.
7. The GALIX tubes: all 14 in one volley, the grenades going out along their tubes.
8. Shoot it with an APFSDS and a HEAT and read the damage report, then a round into the bustle.
9. Traverse, then watch the AAR replay.
10. A vanilla T-80B and Leopard 2A3 in the same mission, untouched.
