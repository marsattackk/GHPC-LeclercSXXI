using System.Collections.Generic;
using System.Linq;
using GHPC.Vehicle;
using GHPC.Weapons;
using LeclercSXXI.Common;
using MelonLoader;
using UnityEngine;

namespace LeclercSXXI
{
	// The crew, ammunition and fuel layout. At spawn, after the armour (whose bustle compartment it takes over) and before
	// LoadoutManager.Start fills the racks:
	// - the interior map (Resources\interior_map.txt): the T-80B's crew onto the model's seats (commander left, gunner
	//   right, driver front left), their seats with them. The T-80B carousel becomes the bustle conveyor's 22 cells (the
	//   rack frame stays fixed while ConveyorLoader moves its slots along the endless chain), the hull wet rack the
	//   18-round drum at the front right. The T-80B's fuel tank pieces are reshaped onto the Leclerc's 10 tanks.
	// - the T-80B carousel's own armour and basket off (no carousel on a Leclerc)
	// - the bustle as its own compartment vented by two blow-off panels (BlowoutRack, GHPC's M1 bustle)
	// - the T-80B's fixed missile slots cleared (the rounds and rack counts: Firepower.cs)
	// - the modules (PortModules): the rammer and the conveyor drive in the bustle, the fuel tanks' hit report.
	internal static partial class ConversionInterior
	{
		internal static readonly InteriorMap.Embedded Map = new InteriorMap.Embedded(
			typeof(LeclercSXXIMod).Assembly,
			LeclercSXXIMod.FriendlyName,
			"LeclercSXXI",
			"T-80B");
		internal static readonly PortModules Modules = new PortModules(
			typeof(LeclercSXXIMod).Assembly,
			LeclercSXXIMod.FriendlyName,
			"T-80B",
			"LeclercSXXI");

		const string Conveyor = "T-80 TURRET FLOOR COLLIDERS/MZ_autoloader/Carousel_Assembly(Rotates)";

		// The bustle: «l'explosion éventuelle des munitions est dirigée vers le haut grâce à deux panneaux détachables libérés
		// par l'explosion» (French Wikipedia, Leclerc). The armour stage's empty "Leclerc bustle compartment" becomes the box
		// (ReplaceCompartment). The panels are the two halves of the left bustle-roof plate (the rig map's detachables
		// BUSTLE_LID_F / _R).
		static readonly BlowoutRack.Settings Bustle = new BlowoutRack.Settings
		{
			Tag = LeclercSXXIMod.FriendlyName,
			Prefix = "LeclercSXXI_",
			RackPath = Conveyor,
			RackName = "bustle conveyor",
			// Restock from the drum: ~22 s a round in all, the drum's retrieval (the T-80B wet rack's 15 s) and this rack's
			// storage 7 s. 8 min for the 22 cells, a low-confidence source (a French wiki mirror and a 2009 blog), and the
			// split is inferred. RestockPriority pauses the transfer while the gun reloads.
			RetrievalDelay = 15f,
			StorageDelay = 7f,
			CompartmentName = "turret bustle",   // GHPC's M1 name
			VolumeName = "Leclerc bustle compartment",
			ReplaceCompartment = "Leclerc bustle compartment",
			UpReference = "T80B_rig/HULL/TURRET",
			// As the T-90M's box: the residual fire 0.5 m for 30 s, the vents' flames at half size (gameplay settings of mine, no source).
			ResidualFlame = 0.5f,
			ResidualBurn = 30f,
			FireScale = 0.5f,
			// Panels: no thickness or mass found. 10 mm steel over the model's panel sizes (front 1.3 x 0.7 m = 73 kg, rear 1.2 x
			// 0.7 m = 60 kg), thrown at the M1's speed for a centre panel (6 m/s) at its flame height (2 m), vents of the panels'
			// area (radius 0.54 / 0.50 m). Inferred / GHPC's M1 values.
			Lids = new[]
			{
				new BlowoutRack.Lid
				{
					Name = "BUSTLE_LID_F",
					ExitName = "blowout front",
					ExitRadius = 0.54f,
					Source = GHPC.Effects.FlammableSourceType.TopLarge,
					MassKg = 73f,
					FlameHeightTrigger = 2f,
					ForceForFlameTrigger = 73f * 6f
				},
				new BlowoutRack.Lid
				{
					Name = "BUSTLE_LID_R",
					ExitName = "blowout rear",
					ExitRadius = 0.50f,
					Source = GHPC.Effects.FlammableSourceType.TopLarge,
					MassKg = 60f,
					FlameHeightTrigger = 2f,
					ForceForFlameTrigger = 60f * 6f
				},
			},
		};

		static bool _logged;

		// Each row runs on its own: one that throws leaves the rest of the interior fitted (ConversionSteps: the main table's
		// "interior" row runs this at spawn).
		static readonly ConversionStep<Vehicle>[] Steps =
		{
			new ConversionStep<Vehicle>
			{
				What = "interior map",
				Otherwise = "the T-80B's crew places, racks and fuel tanks stay",
				Run = Map.Apply,
			},
			new ConversionStep<Vehicle>
			{
				What = "carousel parts",
				Otherwise = "the T-80B carousel's basket and cassette armour stay",
				Run = BaseCarouselOff,
			},
			new ConversionStep<Vehicle>
			{
				What = "bustle box",
				Otherwise = "the bustle is no compartment of its own and has no blow-off panels",
				Run = FitBustle,
			},
			new ConversionStep<Vehicle> { What = "missile slots", Otherwise = "the T-80B's two missile slots stay", Run = NoMissileSlots },
			// Wired two frames later (Conversion.LateSteps), once the sights' views exist.
			new ConversionStep<Vehicle> { What = "modules", Otherwise = "the Leclerc's own modules are missing", Run = BuildModules },
			new ConversionStep<Vehicle>
			{
				What = "x-ray drawings",
				Otherwise = "the T-80B's x-ray drawings of its engine, gun and autoloader stay",
				Run = DetailedXray,
			},
		};

		// Replace only the donor's X-ray drawing. Hit zones, colliders and their damage handlers remain alive.
		static void DetailedXray(Vehicle vehicle)
		{
			var names = new HashSet<string> {
				"Engine_GTD-1000TF",
				"Gearbox_001",
				"Radiator_TransmissionOil_001",
				"Turret_TraverseMotor_Hull mounted parts of 2E26M stabilizer",
				"Gun_Elevation_ManualDrive",
				"Turret_ManualDrive",
				"Autoloader_MZ",
				"Gun_2A46-2",
				"Gun_Breach_Gun_Breach_BitsThatRecoil" };
			foreach (var part in vehicle.GetComponentsInChildren<Transform>(true).Where(candidate => names.Contains(candidate.name)))
				foreach (var visual in part.GetComponentsInChildren<GHPC.AarVisual>(true)) visual.Hidden = true;
		}

		// The base carousel's own hit zones that the Leclerc has no use for: its basket (armour, 3 mm) off, and its cassette armour
		// (colliders on the rack object itself, which must stay because it is the conveyor's rack) off.
		static void BaseCarouselOff(Vehicle vehicle)
		{
			foreach (var child in vehicle.GetComponentsInChildren<Transform>(true))
			{
				if (child.name == "Turret_basket_approximate" && child.GetComponent<Collider>() != null) child.gameObject.SetActive(false);
				else if (child.name == "Carousel_Assembly(Rotates)")
					foreach (var collider in child.GetComponents<Collider>()) if (collider.enabled) collider.enabled = false;
			}
		}

		// The T-80B's two 9M112 missiles sit at fixed carousel slots 26 / 27 (LoadoutManager FixedChoices): gone with the slots
		// and the missile, as the Leclerc has none (its rounds are in Firepower.cs).
		static void NoMissileSlots(Vehicle vehicle)
		{
			var loadout = vehicle.GetComponent<LoadoutManager>();
			if (loadout?.RackLoadouts == null) return;
			foreach (var rack in loadout.RackLoadouts) rack.FixedChoices = new LoadoutManager.RackLoadoutFixedChoice[0];
		}

		// The bustle box (Bustle). What it could not find is said once a session, by the first tank that has a problem.
		static void FitBustle(Vehicle vehicle)
		{
			var problems = new List<string>();
			BlowoutRack.Apply(vehicle, Bustle, null, null, problems);
			if (problems.Count == 0 || _logged) return;
			_logged = true;
			MelonLogger.Warning($"{LeclercSXXIMod.FriendlyName} bustle box: {string.Join("; ", problems)}.");
		}

		static void BuildModules(Vehicle vehicle)
		{
			Modules.Build(vehicle);
		}

		internal static void Apply(Vehicle vehicle)
		{
			ConversionSteps.Run(LeclercSXXIMod.FriendlyName, Steps, vehicle, vehicle.name, atSpawn: true);
		}
	}
}
