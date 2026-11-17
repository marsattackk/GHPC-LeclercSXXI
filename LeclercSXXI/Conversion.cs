using System;
using System.Collections;
using System.Linq;
using GHPC.Vehicle;
using LeclercSXXI.Common;
using MelonLoader;
using UnityEngine;

namespace LeclercSXXI
{
	// Turns a spawned T80B into a Leclerc SXXI, step after step in the order of Steps, then two frames later the steps of
	// LateSteps, which need the components' Start to have run.
	internal static partial class Conversion
	{
		// The T80B rig's bones; the bundle's group meshes are built relative to them.
		static readonly (string group, string path)[] Bones =
		{
			("HULL", "T80B_rig/HULL"),
			("TURRET", "T80B_rig/HULL/TURRET"),
			("GUN", "T80B_rig/HULL/TURRET/gun")
		};
		// Bundle material names of the camo-painted atlases.
		static readonly string[] Painted =
		{
			"leclercsxxi_leclerc_s2_gun",
			"leclercsxxi_leclerc_s2_turret",
			"leclercsxxi_leclerc_s2_body",
			"leclercsxxi_leclerc_sxxi_turret",
			"leclercsxxi_leclerc_s2_dz"
		};

		static readonly ExhaustMove.Outlet[] Exhaust =
		{
			new ExhaustMove.Outlet("Particles/Exhaust T80", new Vector3(-1.43f, 1.66f, -4.85f), 0.5f),
		};

		// What the steps share.
		sealed class Parts
		{
			internal Vehicle Vehicle;
			internal Transform Root;
		}

		// The steps in the order they run (the order is a rule: each one builds on the one before). Every row runs on its own:
		// a step that throws logs its name and what the player gets instead, and the steps after it still run.
		static readonly ConversionStep<Parts>[] Steps =
		{
			new ConversionStep<Parts> { What = "drive", Otherwise = "the T-80B's drive stays", Run = SetMobility },
			new ConversionStep<Parts> { What = "engine audio", Otherwise = "the T-80B's engine sound stays", Run = SetEngineAudio },
			// Before the interior (its bustle compartment becomes the box) and before PortAxes moves the pivots, with the rig at rest.
			new ConversionStep<Parts> { What = "armour", Otherwise = "the T-80B's armour stays", Run = ApplyArmour },
			new ConversionStep<Parts> { What = "base visuals", Otherwise = "the T-80B's body stays drawn", Run = HideBase },
			new ConversionStep<Parts>
			{
				What = "T-80B Luna out",
				Otherwise = "the hidden T-80B Luna lights the ground in night sights",
				Run = p => BaseStrip.TakeOutLuna(p.Vehicle, LeclercSXXIMod.FriendlyName),
				SpawnOnly = true,
			},
			new ConversionStep<Parts> { What = "visuals", Otherwise = "no Leclerc model is drawn", Run = AttachVisuals },
			// After the armour and the visuals (the box's blow-off lids are visual parts, looked up by name), before the racks fill.
			new ConversionStep<Parts>
			{
				What = "interior",
				Otherwise = "the T-80B's crew places and racks stay",
				Run = FitInterior,
				SpawnOnly = true,
				Late = LateText("interior", "it keeps the T-80B's crew places, racks and modules"),
			},
			new ConversionStep<Parts>
			{
				What = "ammo setup",
				Otherwise = "it keeps the T-80B's ammunition",
				Run = FitRounds,
				SpawnOnly = true,
				Late = LateText("ammo setup", "it keeps the T-80B's ammunition"),
			},
			new ConversionStep<Parts> { What = "gun setup", Otherwise = "the T-80B's gun stays", Run = FitGun },
			new ConversionStep<Parts> { What = "paint", Otherwise = "the hull stays on the bundle's material", Run = Paint },
			new ConversionStep<Parts> { What = "glass materials", Otherwise = "the bundle's glass stays", Run = ApplyGlass },
			new ConversionStep<Parts> { What = "smoke launchers", Otherwise = "the T-80B's 8 tubes stay", Run = SeatSmoke },
			new ConversionStep<Parts> { What = "smoke grenade", Otherwise = "the T-80B's 3D6 grenade stays", Run = FitSmokeGrenade },
			new ConversionStep<Parts> { What = "smoke caps", Otherwise = "the tube caps stay shut", Run = FitSmokeCaps },
			new ConversionStep<Parts> { What = "exhaust", Otherwise = "the T-80B's exhaust stays", Run = MoveExhaust },
			new ConversionStep<Parts> { What = "hatches", Otherwise = "the hatch lids stay shut", Run = MoveHatches },
			new ConversionStep<Parts> { What = "turret axis", Otherwise = "the turret turns on the T-80B's pivots", Run = MoveAxes },
			new ConversionStep<Parts> { What = "recoil", Otherwise = "the barrel does not recoil", Run = FitRecoil },
			new ConversionStep<Parts>
			{
				What = "conveyor",
				Otherwise = "the bustle conveyor does not move",
				Run = FitConveyor,
				SpawnOnly = true,
				Late = LateText("conveyor", "the bustle conveyor does not move"),
			},
			new ConversionStep<Parts>
			{
				What = "sight setup",
				Otherwise = "the T-80B's sights stay",
				Run = FitSights,
				SpawnOnly = true,
				Late = LateText("sight setup", "it keeps the T-80B's sights"),
			},
			new ConversionStep<Parts>
			{
				What = "high-explosive fuse setup",
				Otherwise = "the high-explosive round has no air-burst fuse",
				Run = FitFuse,
				SpawnOnly = true,
				Late = LateText("high-explosive fuse setup", "the high-explosive round has no air-burst fuse"),
			},
			new ConversionStep<Parts>
			{
				What = "coax setup",
				Otherwise = "the coax keeps the T-80B's belt",
				Run = FitCoax,
				SpawnOnly = true,
				Late = LateText("coax setup", "the coax keeps the T-80B's belt"),
			},
			// After the last part is in place.
			new ConversionStep<Parts>
			{
				What = "thermal heat",
				Otherwise = "the Leclerc's own parts show cold in thermal sights",
				Run = HeatParts,
			},
		};

		internal static void Convert(Vehicle vehicle, bool atSpawn = false)
		{
			if (LeclercSXXIMod.Visual == null || !ConvertSweep.Mark(vehicle)) return;
			var parts = new Parts { Vehicle = vehicle, Root = vehicle.transform };
			Func<ConversionStep<Parts>, bool> keeps = null;
			DevKeeps(ref keeps);
			ConversionSteps.Run(LeclercSXXIMod.FriendlyName, Steps, parts, vehicle.name, atSpawn, keeps);
			MelonCoroutines.Start(LateSetup(vehicle, atSpawn));
			DevAfter(vehicle, atSpawn);
		}

		// The steps that need the components' Start to have run, two frames after the spawn, in this order. Every row runs on
		// its own, as in Steps.
		static readonly ConversionStep<Vehicle>[] LateSteps =
		{
			// A spawned unit's smoke launcher cached its crew before the crew existed and never fires without a refresh.
			new ConversionStep<Vehicle>
			{
				What = "smoke crew refresh",
				Otherwise = "the smoke launcher may not fire",
				Run = Countermeasures.RefreshCrew,
			},
			new ConversionStep<Vehicle> { What = "voices", Otherwise = "the crew keeps its Russian voices", Run = ConversionVoices.Apply },
			// The views, once the base optics have started; the laser window was placed at spawn ("sight setup").
			new ConversionStep<Vehicle>
			{
				What = "sight fitting",
				Otherwise = "the T-80B's sights stay",
				Run = Sights.Fit,
				SpawnOnly = true,
			},
			// After the sights: the sight heads' hit zones name their views.
			new ConversionStep<Vehicle>
			{
				What = "module wiring",
				Otherwise = "hits on the Leclerc's own modules do nothing",
				Run = WireModules,
				SpawnOnly = true,
			},
			// After the module wiring: the hl120 module is the HL 120's head.
			new ConversionStep<Vehicle>
			{
				What = "HL 120 secours hit zone",
				Otherwise = "the HL 120 secours keeps working when its head is lost",
				Run = Sights.EndSecoursWithHead,
				SpawnOnly = true,
			},
		};

		static IEnumerator LateSetup(Vehicle vehicle, bool atSpawn)
		{
			yield return null;
			yield return null;
			if (vehicle == null) yield break;
			Func<ConversionStep<Vehicle>, bool> keeps = null;
			DevKeeps(ref keeps);
			ConversionSteps.Run(LeclercSXXIMod.FriendlyName, LateSteps, vehicle, vehicle.name, atSpawn, keeps);
		}

		// What a step that only runs at spawn leaves undone, said by ConversionSteps.Run when a vehicle is converted late.
		static string LateText(string step, string note) =>
			$"{LeclercSXXIMod.FriendlyName} {step}: {{0}} was converted after it spawned; {note}.";

		static void SetMobility(Parts parts)
		{
			Drive.Apply(parts.Vehicle);
		}

		static void SetEngineAudio(Parts parts)
		{
			EngineAudio.Apply(parts.Vehicle);
		}

		static void ApplyArmour(Parts parts)
		{
			Armour.Apply(parts.Vehicle);
		}

		// The T-80B's own hull / turret / gun / skirts / stowage / lamps off (BaseRigT80B).
		// The Leclerc's own wheels and track are drawn (GearFollow, from HatchRig.Apply below): the T-80B's go, its bones and physics stay.
		static void HideBase(Parts parts)
		{
			BaseRigT80B.HideBaseVisuals(parts.Root, "LeclercSXXI_");
			BaseRigT80B.HideRunningGear(parts.Root, "LeclercSXXI_");
			DevBaseHidden(parts.Vehicle);
		}

		static void AttachVisuals(Parts parts)
		{
			VisualParts.Attach(parts.Root, LeclercSXXIMod.Visual, "LeclercSXXI_", Bones);
		}

		// Crew, ammunition and fuel (Conversion.Interior.cs).
		static void FitInterior(Parts parts)
		{
			ConversionInterior.Apply(parts.Vehicle);
		}

		// The French 120 mm rounds on the Leopard 2's one-piece models, 22 + 18 (Firepower.cs).
		static void FitRounds(Parts parts)
		{
			Firepower.Apply(parts.Vehicle);
		}

		// The CN120-26: drives, limits, 6 / 8 s reload with the loading dip, HUD name.
		static void FitGun(Parts parts)
		{
			FirepowerGun.Apply(parts.Vehicle);
		}

		// Camo-painted atlases on GHPC's vehicle shader (the T-80B's own material as the template), the rest on the
		// thermal-aware Standard (FLIR) (VehiclePaint).
		// Weathered (the bundle's baked maps) or clean ("Weathered paint", PaintFinish).
		static void Paint(Parts parts)
		{
			PaintFinish.Apply(LeclercSXXIMod.WeatheredPaint);
			DevPaint(parts);
			var template = BaseRigT80B.FindTemplate(parts.Root);
			var flir = Shader.Find("Standard (FLIR)");
			foreach (var part in parts.Root
				.GetComponentsInChildren<Transform>(true)
				.Where(child => child.name.StartsWith("LeclercSXXI_"))
				.ToArray())
				if (part.parent == null || !part.parent.name.StartsWith("LeclercSXXI_"))
					VehiclePaint.ApplyPaint(part.gameObject, template, flir, Painted);
			if (template == null)
				MelonLogger.Warning(
					$"{LeclercSXXIMod.FriendlyName}: no T-80B paint template found; the hull stays on the bundle's material.");
		}

		// The game's own glass / lens materials on the port's (the bundle's material roles).
		static void ApplyGlass(Parts parts)
		{
			VanillaMaterials.Apply(parts.Root, LeclercSXXIMod.Roles);   // by material name, every renderer of the unit
		}

		// The GALIX smoke tubes, their grenade and caps (the crew refresh is a late step): Countermeasures.cs.
		static void SeatSmoke(Parts parts)
		{
			Countermeasures.SeatLaunchers(parts.Vehicle);
		}

		static void FitSmokeGrenade(Parts parts)
		{
			Countermeasures.FitGrenade(parts.Vehicle);
		}

		static void FitSmokeCaps(Parts parts)
		{
			Countermeasures.FitCaps(parts.Root);
		}

		// The T-80B's exhaust (heat haze + smoke) from over its rear deck, where it rippled the hull top under the turret
		// overhang, to the Leclerc's outlet: the cup at the rear left corner (War Thunder model, x -1.65..-1.22, y 1.35..1.66,
		// z -5.03..-4.60), smaller than the gas turbine's plume.
		// inferred: the mouth's centre from the model's cup, and the diesel plume's size (half the T-80B's).
		static void MoveExhaust(Parts parts)
		{
			ExhaustMove.Place(parts.Root, Exhaust);
		}

		// Hatch lids onto their War Thunder hinges, following the base vehicle's hatch animations (after any bone moves).
		static void MoveHatches(Parts parts)
		{
			HatchRig.Apply(parts.Root, LeclercSXXIMod.Rig, "LeclercSXXI_", LeclercSXXIMod.FriendlyName);
		}

		// The real turret ring, trunnion and muzzle (the rig map's axes, from the War Thunder skeleton; PortAxes): the
		// T-80B pivots moved there, the muzzle point onto the levelled barrel, the turret turned about its tilted ring.
		static void MoveAxes(Parts parts)
		{
			var axes = PortAxes.FromRig(LeclercSXXIMod.Rig, BaseRigT80B.TurretPivot, BaseRigT80B.GunPivot, LeclercSXXIMod.FriendlyName);
			if (axes != null) PortAxes.Apply(parts.Vehicle, axes);
			else
				MelonLogger.Warning(
					$"{LeclercSXXIMod.FriendlyName}: bundle's rig map has no axes; the turret turns on the T-80B's pivots.");
		}

		// The barrel onto the T-80B's recoil bone, 400 mm stroke (after the axes moved).
		static void FitRecoil(Parts parts)
		{
			FirepowerGun.Recoil(parts.Vehicle);
		}

		static void FitConveyor(Parts parts)
		{
			ConveyorLoader.Attach(parts.Vehicle);
		}

		static void FitSights(Parts parts)
		{
			Sights.Apply(parts.Vehicle);
		}

		static void FitFuse(Parts parts)
		{
			AirBurst.Attach(parts.Vehicle);
		}

		static void FitCoax(Parts parts)
		{
			Coax.Apply(parts.Vehicle);
		}

		// The modules (Conversion.Interior.cs, built at spawn) wired by hand: GHPC's owners subscribe to their hit zones once.
		static void WireModules(Vehicle vehicle)
		{
			ConversionInterior.Modules.Wire(vehicle);
		}

		// Heat in thermal sights for the Leclerc's own Standard (FLIR) parts (glass, stowage, the coax), added after GHPC's
		// HeatSources collected their renderers: each part a HeatSource at the T-80B's own value, then every source of
		// the unit collects again (ThermalHeat, as the T-90s / T-80UM2). The painted parts' heat is in their AO maps
		// (the deck grilles and exhaust). After the last part is in place.
		static void HeatParts(Parts parts)
		{
			ThermalHeat.Apply(parts.Root, ThermalHeat.PortParts(parts.Root, "LeclercSXXI_"), LeclercSXXIMod.FriendlyName);
		}

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		static partial void DevKeeps(ref Func<ConversionStep<Parts>, bool> keep);
		static partial void DevKeeps(ref Func<ConversionStep<Vehicle>, bool> keep);
		static partial void DevBaseHidden(Vehicle vehicle);
		static partial void DevPaint(Parts parts);
		static partial void DevAfter(Vehicle vehicle, bool atSpawn);
	}
}
