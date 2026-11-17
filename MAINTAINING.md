# Maintaining the Leclerc SXXI mod

For someone who knows C# but hasn't done GHPC modding. The mod is a MelonLoader DLL that adds the Leclerc SXXI as its own
unit: a copy of the game's T-80B that gets converted when it spawns. Model, textures and armour meshes come from an
asset bundle, everything else is the code and data here.

If the log fills with warnings after a game update, go to [docs/game-updates.md](docs/game-updates.md).

## Building

You need Windows (GHPC is Windows-only and the tools here are PowerShell scripts), the .NET SDK (6 or newer, online once
for the 4.7.2 reference assemblies; the CI tests run on SDK 8) and GHPC with MelonLoader 0.7.3 or newer, because the project references the game's
DLLs. Start the game once after installing MelonLoader so it sets itself up: `tools\check-game.ps1` reads its
Mono.Cecil.

```
dotnet build -c Release -p:GameDir="<the game's Bin folder>"
```

`GameDir` is the game's `Bin` folder, the one with `GHPC.exe`, `MelonLoader` and `Mods` in it (not the folder above it).
It defaults to the usual Steam path. The DLL ends up in
`bin\Release\LeclercSXXI.dll`. Copy it to `Mods\LeclercSXXI.dll` in the game.

The bundle (model, textures, armour meshes) is not in this repo and is not built from it: take
`Mods\LeclercSXXI\leclercsxxi` from the latest [release zip](https://github.com/marsattackk/GHPC-LeclercSXXI/releases/latest).
The code was built against the 0.1.2 bundle, which 0.2.0 ships unchanged. Without the bundle's model the mod loads and the
Leclerc stays a complete T-80B: nothing of the conversion is applied.

The DLL logs a stamp when it loads, like `Leclerc SXXI 0.2.0+a1b2c3d.public, game <version>, Steam build <number>`, so a
player's log tells you which build they ran. The version comes from `MelonInfo` in `LeclercSXXI\Mod.cs`, the commit from
`-p:GitCommit=<hash>` (otherwise `unknown`), the flavour from `-p:BuildFlavour` (default `public`).

## Tests

`tests\` covers the bits that don't need the game: the reticle files' expression language, the reticle JSON, MiniJson,
`dm_armour` parsing, the settings migrations and the fire control's maths. It compiles those files straight out of
`Common\` with stand-ins for MelonLoader, so it runs without GHPC and GitHub Actions runs it on every push that touches code (Markdown-only ones skip it). Two tests
read what ships in `Resources\`: `ShippedTextTests.cs` takes `dm_armour.txt` and fails on a line that doesn't read or a
figure the damage model can't take, and `ShippedReticleTests.cs` does the reticles. CI never builds the mod itself,
because that needs the game's DLLs, so build it against your own copy and run `tools\check-game.ps1` before a pull
request.

```
dotnet test tests
```

The two checks run from the repo's folder in PowerShell. `check-game.ps1` builds the mod itself into a temporary folder
(no build is needed first), then looks every game hook of the built DLL up in the game's own DLLs, so it needs the game
with MelonLoader started once (it uses MelonLoader's Mono.Cecil). If the game has never been started it stops with "No
Mono.Cecil.dll under <the game's Bin folder>\MelonLoader. Start the game once so MelonLoader sets itself up."
`check-settings.ps1` only reads the source and the two settings tables; it builds nothing.

```
powershell -ExecutionPolicy Bypass -File tools\check-game.ps1 -GameDir "<the game's Bin folder>"
powershell -ExecutionPolicy Bypass -File tools\check-settings.ps1
```

A second workflow runs `tools\check-settings.ps1` on every push and pull request, Markdown-only ones included, because
the tests skip those and a README edit is the usual way for the settings tables to drift from the code.

The reticle language is described in [docs/reticles.md](docs/reticles.md), and `ExpressionTests.cs` pins it: read both
before changing `ReticleSpec.Expressions.cs`. Every reticle in `Resources\` is also expanded and compared line by line
with `tests\expanded\`. After changing a reticle on purpose, set `LeclercSXXI_UPDATE_EXPANDED=1`, run the tests once and
check the diff is what you meant.

## Layout

```
LeclercSXXI\   the tank itself
Common\        the stamped, sealed code shared with my other ports
Resources\     data embedded in the DLL, file by file in its README.md
tools\         check-game.ps1, check-settings.ps1, verify-release.ps1
tests\         the tests above, a project of their own
docs\          game-updates.md, bundle.md, reticles.md and shared-features.md for you; armour.md and details.md for
               players; the images
```

`LeclercSXXI\` is small, one file per job. `Mod.cs` is the entry point: the settings, the data load and the unit's
registration, with the settings' descriptions in `SettingText.cs`. `Conversion.cs` does the converting, calling the
rest, with `Conversion.Interior.cs` for the crew, racks and fuel and `Conversion.Voices.cs` for the M1IP's crew voices.
`Armour.cs` applies the damage-model armour and builds the bustle compartment. `Firepower.cs` holds the rounds, counts and
the ammunition menu. Its parts are `Firepower.Gun.cs` (the CN120-26: limits, drives, depression, recoil and the shot),
`Firepower.Coax.cs` (the 12.7 mm and its belts), `Firepower.Autoloader.cs` (the conveyor, door and rammer and the restock
from the drum), `Firepower.Motion.cs` (how they move), `Firepower.Reload.cs` (the loading dip), `Firepower.Restock.cs`
(the drum-to-bustle transfer giving way to the gun's reload), `Firepower.Sounds.cs` (the reload sound) and
`Firepower.StorageLoad.cs` (the hand-load sound when a round comes from outside the conveyor). `Drive.cs` is the engine, gearbox and brakes, with
`Drive.Audio.cs` for the engine sound. `Countermeasures.cs` is the GALIX tubes: how many, which way each points and the
one volley. `Sights.cs` fits the sights, with `Sights.Hl130.cs` for the gunner's day reticle, `Sights.HlDisplay.cs` for its display,
`Sights.Secours.cs` for the emergency view, `Sights.EmergencySight.cs` for its reticle by round and `Sights.Hl120.cs` for
the commander's end. There the HL 120 head is a still prop: `FitCommander`
only raises the free-look eye over it and returns, and the observation views written below that return never run.
`Reticles.cs` is the shared reticle handle. There's no `Commander.cs`: nothing turns the cupola.

`tools\verify-release.ps1` rebuilds a tag and compares it with a release DLL or zip (the last step of Releasing).
[docs/shared-features.md](docs/shared-features.md) lists the features in `Common\` and whether the tank switches each
on.

Where to change things:

| What | Where |
|---|---|
| Tunable numbers | `Firepower.cs` (rounds, mix), `Firepower.Gun.cs` (gun), `Firepower.Coax.cs` (belts), `Drive.cs` (engine and gears) |
| A setting's description | `SettingText.cs`, except three that live in `Common`: `Weathered paint` (`PaintFinish.cs`), `Campaign prevalence` (`UnitRegistry.cs`) and `Check for updates` (`UpdateCheck.cs`) |
| Armour values, hatches, glass, smoke points | the text files in `Resources\` |

Every method named `Dev...` is an empty partial method, a hook for my own test builds. That code isn't in this repo, so
ignore the hooks. The compiler removes the declaration and every call to it, so a normal build contains nothing of them.
This lists them: `Select-String -Path LeclercSXXI\*.cs -Pattern "partial void Dev"`. You can delete one too. A hook with
a `ref` argument goes together with the local it sets.

`Common\` is the machinery, fed by settings objects from `LeclercSXXI\`, so the Leclerc's numbers stay in
`LeclercSXXI\`. It is shared by my GHPC mods and stamped into each repo, so it is never edited here: the same file is the
same wherever it appears, and each tank gets the subset it needs, so it holds code the Leclerc doesn't use.
[docs/shared-features.md](docs/shared-features.md) says which features those are and which the tank switches on. It is
sealed: `Common\STAMP.txt` lists a hash for every file and `tests\CommonSealTests.cs` fails, naming the file, when one
differs, so a pull request that edits a `Common\` file fails that test by design. If something there needs changing, open
an issue or a pull request that says what and why. The text of it is the proposal. I make the change in the copy my
mods share (not in this repo) and put it into each of them. The commit hash in `STAMP.txt` is that copy's, so you can't
look it up.

The features page lists a feature as on when a file in the tank folder names it. Two rows are wrong for that reason:
"Commander's cupola" and "Target designation" are named only in the HL 120 code that never runs (`FitCommander` returns
before it), so the tank doesn't use them. The same goes for the HL 120 part of the machine-gun and thermal rows.

The Leclerc is built on the T-80B, like my T-80UM2, but it isn't a T-80UM2 and its settings and tuning don't carry over.
The base has a few things that are wrong for it. The hull is 1.08 m longer than the T-80B's, so the base's volumes,
modules and racks don't reach the ends, and the volumes come from the imported model, with each module's box fitted by
moving and scaling it, never by its centre. The turret ring is smaller (1.92 m against 2.23 m) and about 0.5 m from the
base's, so the base's ring colliders are the wrong size. The road wheels are staggered 6 cm between the sides. There's
no carousel and no missile, and the T-80B's missile feed is removed. And the base leaves Soviet strings behind (HUD text,
fonts, reticles, voices, gun and round names, the thermal chain): any a player can see is a bug.

## What happens when a Leclerc SXXI spawns

At game start `LeclercSXXIMod.OnInitializeMelon` makes the settings (`CreateSettings`), stops there if `Enabled` is off,
opens the bundle and reads the text data (`LoadData`) and registers a unit (`RegisterUnit`) `LeclercSXXI` based on the
`T80B` with the `M60A1`'s army (GHPC has no French army, and the M60A1's puts it on the American side). `UnitRegistry`
copies the T-80B's entry under that name so Instant Action, the customiser and the campaign offer it, and re-adds it
whenever a scene or spawner turns up without it. The campaign roster is the American side's, taken from `CampaignConfig`
(`Add to campaign roster`, `Campaign prevalence`, `Campaign unlock after missions`) and redone at every scene load.

At spawn, `SpawnPatch` (in `Common`) on `UnitSpawner.SpawnUnit` renames the unit and calls `Conversion.Convert`. That works
through the `Steps` table in `Conversion.cs`, in this order: drive, engine audio, armour, base visuals, visuals, interior,
ammo setup, gun setup, paint, glass materials, smoke launchers, smoke grenade, smoke caps, exhaust, hatches, turret axis,
recoil, conveyor, sight setup (the HL 130's laser window, placed while the turret is at rest), high-explosive fuse setup,
coax setup and thermal heat. The order is a rule, because each step builds on the one before. The armour comes before the interior because its bustle
compartment becomes the box, and the visuals come before the interior because the box's blow-off panels are visual parts
that it looks up by name. The ammo goes after the interior, which fills the racks, and the thermal heat goes last, once
every part is in place.

Two frames later, once the game's own components have started, `LateSetup` works through the `LateSteps` table: smoke
crew refresh (a spawned unit's smoke launcher cached its crew before the crew existed), voices, sight fitting (the HL 130's
and HL 120's views on the T-80B's optics), module wiring (the Leclerc's own modules' hit zones, which name those views) and
the HL 120 secours hit zone (losing the HL 120's head ends the emergency view).

Each row of either table runs on its own. `ConversionSteps.Run` (in `Common`) catches a step that throws and logs
`Leclerc SXXI <step> failed (<what the player gets instead>): <exception>`, and the rest carry on. A step catches nothing
itself, so that line is the one you see. The interior row runs a small table of its own the same way
(`Conversion.Interior.cs`): interior map, carousel parts, bustle box, missile slots, modules and x-ray drawings.

Six rows of `Steps` are `SpawnOnly`: interior, ammo setup, conveyor, sight setup, fuse setup and coax setup. GHPC builds
the unit's weapon, camera and rack lists straight after the spawn, and a part added later never makes it in. The three
sight and module rows of `LateSteps` only finish what those started, so they are skipped too, without a word of their own. A Leclerc the spawn
patch missed is caught at `PlayerReady` by `ConvertSweep` (in `Common`, started from `OnSceneWasLoaded`) and gets every
other step. For each spawn-only step it skips, the log says `Leclerc SXXI <step>: <unit> was converted after it spawned`
and what the unit keeps from the T-80B.

Parts borrowed from other vehicles (the Leopard 2's rounds, shot sound and M82 grenade, the Leopard 2A3's heat, the
M1IP's voices and 12.7 mm Browning, the Abrams' loader sounds) are copied at the main menu by `DonorParts` (in `Common`),
because GHPC can unload those vehicles between missions. A NATO tank gets NATO parts: the T-80B is only the base nobody
sees.

Private game members are read through `Common\Reflect.cs`, which warns once instead of throwing when a name has gone.
Patches that run every frame report through `Common\PatchGuard.cs` so one throw doesn't kill the game's loop.
`tools\check-game.ps1` checks all of it against a game build without starting the game. If you add a `Reflect` call
whose member name comes from a variable, put the names in `tools\check-game-list.txt` with a `covers` line.

## Settings

MelonPreferences in the `[Leclerc SXXI]` category of `UserData\MelonPreferences.cfg`. The README has the player wording
and this table says where each one lives. `tools\check-settings.ps1` checks both tables against the code. The
descriptions of three settings live in `Common` (`Weathered paint`, `Campaign prevalence`, `Check for updates`).

| Setting | Defined in | Read by |
|---|---|---|
| `Enabled` | `Mod.cs` | everything, off means the mod does nothing |
| `Add to campaign roster`, `Campaign prevalence`, `Campaign unlock after missions` | `LeclercSXXI\Mod.cs`, `Common\UnitRegistry.cs` (`CampaignConfig`, made for the "American" side) | the American side's campaign roster, re-read at each mission load |
| `Weathered paint` | `LeclercSXXI\Mod.cs`, `Common\PaintFinish.cs` | `Conversion` (the paint step) |
| `APFSDS` | `LeclercSXXI\Mod.cs` | `Firepower`, the default round where the game doesn't pick one |
| `Force APFSDS` | `LeclercSXXI\Mod.cs` | `Firepower` (`ForceApfsds`): the campaign's roll gives the `APFSDS` round |
| `Check for updates` | `Common\UpdateCheck.cs` | asks GitHub once per start, one console line |
| `ConfigVersion` | `LeclercSXXI\Mod.cs`, `Common\PortSettings.cs` | hidden, the version that last wrote the section |

Most only apply to Leclercs spawned after the change. There's no `Armour preset`: the Leclerc has one set of armour
figures. Two catches when you add or change a setting:

- MelonLoader writes the old default into the player's file and keeps it. To move players to a new default, add a
  `SettingsMigration` to `Migrations` in `Mod.cs`. It only changes a value still at the old default, once.
- A decimal-comma Windows can leave `"0,3"` in the file, which MelonLoader quietly ignores. `PortSettings.Finish` reads
  it anyway, so call `Finish` after all the entries exist.

## Releasing

1. Bump the version in `MelonInfo` at the top of `LeclercSXXI\Mod.cs`. `Repo` next to it is what the update check asks.
   The version lives only there: the README doesn't carry one. If the bundle changed, put its row into the hash table in
   [docs/bundle.md](docs/bundle.md) in this same commit (the hash is known before the build). If it didn't, the row repeats
   the last hash. Commit.
2. Get the commit's 7-character short hash with `git rev-parse --short=7 HEAD` and build with it:
   `dotnet build -c Release -p:GameDir="<the game's Bin folder>" -p:GitCommit=<that hash>`. The hash goes into the DLL's
   stamp.
3. Install it, start a mission with a Leclerc in it (Instant Action, or the Showcase) and read `MelonLoader\Latest.log`:
   the stamp first, then no warnings or errors from the mod.
4. Zip it as `LeclercSXXI_<version>.zip` with this layout inside, forward slashes: `Mods/LeclercSXXI.dll` and
   `Mods/LeclercSXXI/leclercsxxi` (the bundle from the latest release). In a folder that holds that `Mods` folder:
   `tar -a -c -f LeclercSXXI_<version>.zip Mods`.
5. The armour numbers, hatches, glass and smoke points are text in `Resources\`, so changing them is a rebuild of the DLL
   and nothing else. The bundle only has to change with the model, and that needs the Unity project, which isn't in this
   repo. Renaming an armour piece or a visual node is a model change too. [docs/bundle.md](docs/bundle.md) says which edits
   are safe, and the check for the hash table is in that file.
6. Tag the built commit `v<version>` (like `v0.2.0`) and push the tag. On GitHub create a release from the tag: attach the
   zip, and write the release notes, what changed for the player, a line or two each.
7. Check the result: `powershell -ExecutionPolicy Bypass -File tools\verify-release.ps1 -Tag v<version> -Release
   LeclercSXXI_<version>.zip -GameDir "<the game's Bin folder>"` rebuilds the tag in a temporary folder with the commit the
   release's own stamp carries. Expect IDENTICAL: a release built from its tag rebuilds byte for byte. DIFFERENT means the
   SDK or the source differs from what built the release, and then you compare the decompiled code.
