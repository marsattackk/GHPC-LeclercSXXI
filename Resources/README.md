# Resources

Data files embedded in the DLL. The csproj embeds every file here under its plain name except this README, so a new
file only has to be dropped in. The five text tables at the end are read from the DLL first and from the bundle only if the
DLL has no copy. The reticle files (`*.jsonc`) are written in the language described in
[docs/reticles.md](../docs/reticles.md).

| File | What it holds | Read by |
|---|---|---|
| `gun_depression.txt` | gun depression by turret angle, for the hull profile | `GunSetup`, name set in `Firepower.Gun` |
| `interior_map.txt` | where each crew member and interior part moves to | `InteriorMap`, held by `Conversion.Interior` |
| `modules_built.json` | the Leclerc's damageable modules and what they hook into | `PortModules`, held by `Conversion.Interior` |
| `hl130_day.jsonc` | HL 130 day reticle: the cross, the range plates, the stadiametric scale and the display box | `Sights.Hl130`, `Sights.HlDisplay` |
| `hl130_thermal.jsonc` | HL 130 thermal reticle and its readout row | `Sights.HlDisplay` |
| `hl130_chassis.jsonc` | the hull symbol in the HL 130's display | `Sights.HlDisplay`, turned by the hull's angle to the turret |
| `hl120_secours.jsonc` | the emergency reticle of the commander's HL 120, with a range scale for each round | `Sights.EmergencySight` |
| `reload_6_interior.wav`, `reload_6_exterior.wav` | the reload sound for 6 s, as heard from inside and outside | `Firepower.Sounds`, played by `ReloadSound` |
| `reload_8_interior.wav`, `reload_8_exterior.wav` | the same for the 8 s reload on a change of round | `Firepower.Sounds`, played by `ReloadSound` |
| `round_oflf1_co.png`, `round_oflf2_co.png` | stencil textures for the OFL 120 F1 and F2 darts | `Firepower`, applied by `PortAmmo` |
| `round_oecc_co.png` | stencil texture for the OECC 120 F1 HEAT-MP round | `Firepower`, applied by `PortAmmo` |
| `round_expl_co.png` | stencil texture for the 120 EXPL F1 HE round | `Firepower`, applied by `PortAmmo` |
| `round_meshes.txt` | index of the round mesh pieces below, by round | `PortAmmo.ApplyShape` |
| `round_mesh_ofl_rod.mesh` | the OFL dart's rod, drawn out to its real length | `PortAmmo`, via `round_meshes.txt` |
| `round_mesh_expl_shell.mesh` | the 120 EXPL F1's pointed shell | `PortAmmo`, via `round_meshes.txt` |
| `smoke_directions.txt` | the direction each GALIX grenade leaves its tube in, one line per tube | `SmokeLaunchers.LoadResource`, called from `LeclercSXXIMod.LoadData`, used by `Countermeasures` |
| `dm_armour.txt` | the armour pieces: mode, thickness figures, material, name, source | `PortText`, before the bundle's copy (`Armour.Load`) |
| `vanilla_edits.txt` | which of the T-80B's armour and parts go, and what replaces them | `PortText`, before the bundle's copy (`Armour.Load`) |
| `rig_map.txt` | the model's moving parts and axes: hatches, running gear, turret ring, trunnion, muzzle | `PortText`, before the bundle's copy (`LeclercSXXIMod.LoadData`) |
| `material_roles.txt` | which bundle materials take the game's own glass and lens materials | `PortText`, before the bundle's copy (`LeclercSXXIMod.LoadData`) |
| `smoke_points.txt` | the 14 GALIX tubes' launch points, in the turret's space | `PortText`, before the bundle's copy (`SmokeLaunchers.Load`) |
