using System;
using System.Collections.Generic;
using System.Linq;
using GHPC.Effects;
using GHPC.Vehicle;
using GHPC.Weapons;
using LeclercSXXI.Common;
using MelonLoader;
using UnityEngine;

namespace LeclercSXXI
{
	internal static class Interior
	{
		internal static readonly InteriorMap.Embedded Map = new InteriorMap.Embedded(typeof(LeclercSXXIMod).Assembly, LeclercSXXIMod.FriendlyName, "LeclercSXXI", "T-80B");
		internal static readonly PortModules Modules = new PortModules(typeof(LeclercSXXIMod).Assembly, LeclercSXXIMod.FriendlyName, "T-80B", "LeclercSXXI");

		const string Conveyor = "T-80 TURRET FLOOR COLLIDERS/MZ_autoloader/Carousel_Assembly(Rotates)";

		static readonly BlowoutRack.Settings Bustle = new BlowoutRack.Settings
		{
			Prefix = "LeclercSXXI_",
			RackPath = Conveyor,
			RackName = "bustle conveyor",
			// 22 s a round from the drum to the conveyor, inferred from the 8 minutes quoted for a full load
			RetrievalDelay = 15f,
			StorageDelay = 7f,
			CompartmentName = "turret bustle",
			VolumeName = "Leclerc bustle compartment",
			ReplaceCompartment = "Leclerc bustle compartment",
			UpReference = "T80B_rig/HULL/TURRET",
			ResidualFlame = 0.5f,
			ResidualBurn = 30f,
			Lids = new[]
			{
				new BlowoutRack.Lid { Name = "BUSTLE_LID_F", ExitName = "blowout front", ExitRadius = 0.54f, Source = FlammableSourceType.TopLarge, MassKg = 73f, FlameHeightTrigger = 2f, ForceForFlameTrigger = 73f * 6f },
				new BlowoutRack.Lid { Name = "BUSTLE_LID_R", ExitName = "blowout rear", ExitRadius = 0.50f, Source = FlammableSourceType.TopLarge, MassKg = 60f, FlameHeightTrigger = 2f, ForceForFlameTrigger = 60f * 6f },
			},
		};

		static bool _logged;

		static void DetailedXray(Vehicle vehicle)
		{
			var names = new HashSet<string> {
				"Engine_GTD-1000TF", "Gearbox_001", "Radiator_TransmissionOil_001",
				"Turret_TraverseMotor_Hull mounted parts of 2E26M stabilizer",
				"Gun_Elevation_ManualDrive", "Turret_ManualDrive", "Autoloader_MZ",
				"Gun_2A46-2", "Gun_Breach_Gun_Breach_BitsThatRecoil",
				"autoloader rammer", "conveyor drive" };
			foreach (var t in vehicle.GetComponentsInChildren<Transform>(true).Where(t => names.Contains(t.name)))
				foreach (var visual in t.GetComponentsInChildren<GHPC.AarVisual>(true)) visual.Hidden = true;
		}

		static void BaseCarouselOff(Transform root)
		{
			foreach (var t in root.GetComponentsInChildren<Transform>(true))
			{
				if (t.name == "Turret_basket_approximate" && t.GetComponent<Collider>() != null) t.gameObject.SetActive(false);
				else if (t.name == "Carousel_Assembly(Rotates)")
					foreach (var c in t.GetComponents<Collider>()) if (c.enabled) c.enabled = false;
			}
		}

		static void NoMissileSlots(Vehicle vehicle)
		{
			var lm = vehicle.GetComponent<LoadoutManager>();
			if (lm?.RackLoadouts == null) return;
			foreach (var rl in lm.RackLoadouts) rl.FixedChoices = new LoadoutManager.RackLoadoutFixedChoice[0];
		}

		internal static void Apply(Vehicle vehicle)
		{
			var problems = new List<string>();
			try { Map.Apply(vehicle); }
			catch (Exception e) { MelonLogger.Error($"{LeclercSXXIMod.FriendlyName} interior failed (the T-80B's places kept): " + e); }
			try { BaseCarouselOff(vehicle.transform); }
			catch (Exception e) { MelonLogger.Error($"{LeclercSXXIMod.FriendlyName} carousel parts failed: " + e); }
			try { BlowoutRack.Apply(vehicle, Bustle, null, null, problems); }
			catch (Exception e) { MelonLogger.Error($"{LeclercSXXIMod.FriendlyName} bustle box failed: " + e); }
			try { NoMissileSlots(vehicle); }
			catch (Exception e) { MelonLogger.Error($"{LeclercSXXIMod.FriendlyName} missile slots failed: " + e); }
			// Wired by Sights once its views exist (the HL 120 copies and observation views are made two frames later).
			try { Modules.Build(vehicle); }
			catch (Exception e) { MelonLogger.Error($"{LeclercSXXIMod.FriendlyName} modules failed: " + e); }
			DetailedXray(vehicle);
			if (problems.Count > 0 && !_logged) MelonLogger.Warning($"{LeclercSXXIMod.FriendlyName} bustle box: {string.Join("; ", problems)}.");
			_logged = true;
		}
	}
}
