# The asset bundle

The model, textures and armour meshes live in one Unity asset bundle, `Mods\LeclercSXXI\leclercsxxi`. It isn't in this
repo and neither is the Unity project it came from, so this page is what the code expects to find in it. If you rebuild
it, this is what to build. Where it says "silently", nothing goes in the log and the tank just looks wrong.

Built with Unity **2022.3.62f2**, the same as GHPC. A bundle has to match the game's Unity version (see
[game-updates.md](game-updates.md)).

## What gets loaded

`LeclercSXXI\Mod.cs` opens the bundle through `BundleAssets` and loads the model, and `LeclercSXXI\Armour.cs` the armour,
each prefab by name:

| Asset | Needed | If missing |
|---|---|---|
| `LeclercSXXI_visual` | yes | error, no model |
| `LeclercSXXI_armor` | no | warning, the T-80B's armour stays |

The text data is in `Resources\` as `<name>.txt` and goes into the DLL. `PortText` reads the DLL's copy first and only
falls back to a `TextAsset` of the same name in the bundle. The bundle the releases ship still carries a copy of
each, because the versions before 0.2.0 read them from there. Five of the six are in `Resources\` as well (there is no
`era_links.txt` there). Where both exist they hold the same data. Edit the one in `Resources\` and rebuild the DLL, no
Unity involved:

| File | Needed | If missing |
|---|---|---|
| `dm_armour` | with `LeclercSXXI_armor` | warning `armour is incomplete; keeping the donor armour`, the T-80B's armour stays |
| `vanilla_edits` | with `LeclercSXXI_armor` | the same warning, and the T-80B's armour stays |
| `material_roles` | no | warning `no material roles; glass keeps the bundle's own materials` |
| `rig_map` | no | warning `no rig map; the hatches stay closed`, and nothing the rig map names moves (hatches, running gear, the turret's real pivots) |
| `era_links` | no | nothing, and nothing reads it: no piece uses the `era` mode |
| `smoke_points` | no | warning, grenades leave from the T-80B's tubes |

The catch is that most of them name things in the bundle. Numbers, armour types, x-ray colours, roles, donors, hinge
angles and smoke points are safe to change on their own. Renaming an armour piece, or pointing a line at a different
node or material, only works if the bundle has that name too. Otherwise it's a new bundle, built with the renamed pieces.

Empty lines and lines starting `#` are skipped. A bad line in `dm_armour`, `vanilla_edits` or `rig_map` is skipped
with a warning naming the line, and the rest loads. Numbers use a full stop for the decimal point. `<TAB>` below means
a real tab, no spaces around it.

## `LeclercSXXI_visual`

`Conversion` (its visuals step) instantiates it, moves these children of the root onto the base's bones and throws the
rest away. Each moved part gets `LeclercSXXI_` in front of its name and its local transform reset, so model each one in
its bone's own space.

| Node | Goes onto | If missing |
|---|---|---|
| `HULL`, `TURRET`, `GUN` | the base's hull, turret and gun bones | silently, no hull / turret / gun |

- The hatch lid, the radio whips, the HL 120's head, the road wheels, suspension arms and tracks, and the two blow-off
  panels of the bustle are found later by their names, as `LeclercSXXI_<name>` or `<name>` anywhere under the unit. The
  gunner's lid is `HATCH_GUNNER`, the head `HL120_HEAD`, the whips `WHIP_01` and `WHIP_02`, the wheels `GEAR_WHEEL_L_01` to
  `_R_06` and so on, and the panels `BUSTLE_LID_F` and `BUSTLE_LID_R`. They're listed in `rig_map` where they move, and
  the bustle box looks the panels up by name when it builds. The tracks are skinned meshes.
- The blow-off panels are cut out of the turret mesh, a 1.3 × 0.7 m box for the front one and 1.2 × 0.7 m for the rear one,
  and each hangs on its own pivot.
- The windows of the sights and periscopes aren't modelled glass: the bundle build generates them from the measured
  corners of each pane. That's why `material_roles` names a window material for each.
- LODs: each `LODGroup` with one renderer in LOD0 and one in LOD1 gets LOD0's materials on LOD1, so LOD1 needs as many
  submeshes as LOD0 has materials.

## Materials

Matched with `StartsWith` unless it says exact. Everything is prefixed `leclercsxxi_`.

- The five paint materials (`leclerc_s2_gun`, `leclerc_s2_turret`, `leclerc_s2_body`, `leclerc_sxxi_turret` and
  `leclerc_s2_dz`) are replaced with a copy of the base vehicle's `GHPC/VehicleShader` material, taking your `_MainTex`,
  `_BumpMap`, `_MetallicGlossMap`, `_DetailMask` (paint mask) and `_OcclusionMap`. The camouflage is a three-tone tile laid
  over the atlas UVs.
- The weathering is baked by the game at spawn from the clean maps, which is what `Weathered paint` switches. The
  released bundle doesn't carry the `*_albedo_worn` and `*_metal_worn` maps.
- `leclercsxxi_hatch_inner*` is the inside of the hatch lids. `HatchUndersides` gives them a flat patch of the atlas of their own, because the model's UVs put them on bolt detail.
- Anything on those parts using plain `Standard` is switched to GHPC's `Standard (FLIR)` so it shows in thermal.
- Names in the first column of `material_roles` are matched exactly.

## `LeclercSXXI_armor`

Shapes only, cut from War Thunder's damage model of the Leclerc SXXI. The numbers come from `dm_armour`. `DmArmourApply`
applies `vanilla_edits` first, then puts the pieces under the root's `HULL`, `TURRET` and `GUN` onto the matching bone
and collider group.

Each piece is a direct child of its group, with a readable mesh, named `dm_<anything>|<piece>`. `<piece>` is the key in
`dm_armour` and becomes the object's name. Children not starting `dm_` are skipped, which keeps the `reshape|<name>`
meshes out of the armour. `geometry` pieces are measured through the mesh, so it has to be closed.

## Text files

### `dm_armour`

One piece per line, tab separated, at least 11 fields:

```
<piece> <mode> <ke> <ce> <ke after> <ce after> <angle> <material> <x-ray> <name> <source> [<slat>]
```

- `mode`: `geometry` (`ke` and `ce` are multipliers per mm of path), `composite` (uniform, convex collider), `drawn`
  (x-ray and AAR only, no armour), anything else uniform (the bundle uses `nominal`). There's an `era` mode as well, with
  `ke after` and `ce after` for once a brick has gone off, but the Leclerc has no reactive armour and uses none.
- `ke`, `ce`: mm of rolled steel against kinetic and chemical rounds.
- `angle`: `1` to count the hit angle on a uniform piece.
- `material`: `armour_steel`, `mild_steel`, `plain`, `composite`, `filling`, `filling_steel` or `liner`, all the mod's
  own armour types. Empty for `drawn`.
- `x-ray`: empty (not drawn), `template` (GHPC's see-through material) or `r,g,b[,a]` (a tinted copy).
- `name`: the label in the armour view and damage report. `source` isn't read. `slat`: `1` for slat armour.

There's one set of figures and no preset to choose between.

```
turret_side_dm<TAB>nominal<TAB>30<TAB>30<TAB>0<TAB>0<TAB>1<TAB>armour_steel<TAB><TAB>sponson wall<TAB><TAB>0
```

### `vanilla_edits`

What to do to the T-80B's own armour, by collider object name, `|` separated. The pieces War Thunder's model replaces
are switched off by the code. The Leclerc's list is mostly `off` lines and three `reshape` ones.

| Line | Does |
|---|---|
| `off\|name` | switches the piece and its children off |
| `strip\|name` | removes its colliders, armour and renderer, keeps the children (done last) |
| `reshape\|name` | gives it the mesh of `reshape\|name` in `LeclercSXXI_armor` |
| `xform\|name\|m0,...,m15` | moves it by a `Matrix4x4` (column-major), in the unit's root space at rest |
| `bone\|path\|group\|dx,dy,dz` | moves a bone and the colliders following it, in metres (`group` is a label, not read) |

### `material_roles`

```
<bundle material><TAB><role><TAB><vanilla material or -><TAB><donor units, comma separated>
```

The role is only for the log. `-` makes the line a note. Donors are unit keys like `T80B` or `LEO2A3`, tried in order.

### `rig_map`

`|` separated, positions in the unit's root space, extra trailing fields ignored.

| Line | Fields |
|---|---|
| `gear\|mode` | `base` (leave it, the default), `follow` (wheels and tracks follow the suspension) or `port` (park parts on bones). The Leclerc uses `follow` |
| `axes\|turret\|gun\|muzzle\|pitch` | the turret ring, the gun's trunnion and the muzzle where War Thunder has them, and the ring's tilt in degrees. The turret turns about the tilted ring and the gun and muzzle move there |
| `follow\|restY\|uvX,uvY\|radius\|rimGap\|riseLimit` | the wheel numbers `GearFollow` needs |
| `fpart\|id\|node\|kind\|drive\|pivot\|target\|mate` | one moving part, `kind` is `wheel`, `arm`, `rod`, `spin` or `track` (a track's id ends `_l` or `_r`) |
| `hatch\|?\|role\|node\|baseBone\|hinge\|axis\|angle\|lift` | one hatch, driven by the base bone's `Openness`: lifts for the first 30 %, then turns. The Leclerc has the gunner's only |
| `mount\|?\|node\|bone\|pivot\|axis` | a part with a pivot of its own, here the HL 120's head and the gun barrel. Nothing drives the head, it's a still prop |
| `part\|?\|node\|bone\|qx,qy,qz,qw` | `port` only, parks the node on the bone |
| `whip\|?\|node\|base\|tip\|rigid` | the radio whip |
| `flap\|?\|node\|a\|b\|?\|limit\|taper\|ramp\|lag` | a hanging flap on a jiggle bone, `lag` optional |

`?` fields aren't read. What `rigid`, `taper`, `ramp` and `lag` do is in `HatchRig.Whip` and `HatchRig.Flap`.

### `era_links` and `era_gone`

The Leclerc has no reactive armour and no piece in `dm_armour` uses the `era` mode, so nothing reads either file and
there's no `era_gone`. The bundle carries an `era_links` with only its comment line, and `Resources\` has none. A file
with entries would be read the way the T-90s' is: `<armour piece><TAB><visual brick>`, tying each `era` piece to its
brick in the visual. The bustle's NERA boxes aren't in it. They're ordinary `nominal` pieces in `dm_armour` that nothing sets off.

### `smoke_points`

Floats separated by any spaces, commas, tabs or newlines, three per tube mouth, in the turret bone's space. There are 14
tubes. Left and right go by the sign of x, then top down, matched to the T-80B's slots, which `Conversion` first grows
from 8 to 14. The grenades' directions are in `Resources\smoke_directions.txt`, one line per tube in the same order.
One bad number drops the whole list, since it would shift every tube after it.

## Released bundles

SHA-256 of `Mods/LeclercSXXI/leclercsxxi` in each release zip. When a release changes the bundle, add a row.

| Version | Bundle |
|---|---|
| 0.1.0 | `df3324dcab94be4b17f8d10605659f1d998e488ed0b739ec6941a8a6199a8c0e` |
| 0.1.1 | `de5a338a1794e1b34d66b3bd8eebdd619dcf0e801e7fdc078db2b717971815c5` |
| 0.1.2 | `1b9e8a91b64ccdddc2794e035e0c089294d2eaba2b2ac270d9aa50fd985486ea` |
| 0.2.0 | `1b9e8a91b64ccdddc2794e035e0c089294d2eaba2b2ac270d9aa50fd985486ea` |
| 0.2.1 | `1b9e8a91b64ccdddc2794e035e0c089294d2eaba2b2ac270d9aa50fd985486ea` |
| 0.2.2 | `7a97a5fdd670663b19523032618a23ecf2bcd481db97af8f188a96ddce771772` |
| 0.3.0 | `b23ccc841f324c59a229dbbb1203c5a83aee4e2c36cbdb211e9938e027a5e6c1` |

0.2.0 and 0.2.1 ship the same bundle file as 0.1.2, byte for byte (191,848,931 bytes). What changed in 0.2.0 is the DLL,
which now also carries the five text tables, `dm_armour`, `vanilla_edits`, `material_roles`, `rig_map` and
`smoke_points`, and reads them first. The bundle's own copies are only the fallback.

0.2.2's bundle was packed again from the same Unity project. Every object in it is the same as before and only the file's
bytes differ, so a 0.2.1 bundle works with the 0.2.2 DLL.

```
Expand-Archive LeclercSXXI_<version>.zip -DestinationPath check
(Get-FileHash check\Mods\LeclercSXXI\leclercsxxi -Algorithm SHA256).Hash.ToLower()
```
