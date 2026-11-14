using System.IO;
using System.Linq;
using GHPC.Vehicle;
using LeclercSXXI.Common;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(LeclercSXXI.LeclercSXXIMod), "Leclerc SXXI", "0.1.2", "marsattackk")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace LeclercSXXI
{
	public class LeclercSXXIMod : MelonMod
	{
		internal const string UnitKey = "LeclercSXXI";
		internal const string FriendlyName = "Leclerc SXXI";
		const string BaseUnit = "T80B";
		const string ArmyFrom = "M60A1";
		internal static readonly string[] SmokeFrom = { "LEO2A3", "LEO2A4", "LEO2" };

		static readonly (string group, string path)[] Bones = { ("HULL", "T80B_rig/HULL"), ("TURRET", "T80B_rig/HULL/TURRET"), ("GUN", "T80B_rig/HULL/TURRET/gun") };
		static readonly string[] Painted = { "leclercsxxi_leclerc_s2_gun", "leclercsxxi_leclerc_s2_turret", "leclercsxxi_leclerc_s2_body", "leclercsxxi_leclerc_sxxi_turret", "leclercsxxi_leclerc_s2_dz" };

		static readonly ExhaustMove.Outlet[] Exhaust = { new ExhaustMove.Outlet("Particles/Exhaust T80", new Vector3(-1.43f, 1.66f, -4.85f), 0.5f) };

		internal static GameObject Visual, Armor;
		internal static RigMap Rig;
		internal static MaterialRoles Roles;
		internal static Vector3[] SmokePoints;
		internal static Vector3[] SmokeDirections;
		internal static MelonPreferences_Entry<bool> Enabled;
		internal static MelonPreferences_Entry<bool> WeatheredPaint;

		static readonly string[] MenuScenes = { "MainMenu2_Scene", "MainMenu2-1_Scene", "LOADER_MENU", "LOADER_INITIAL", "t64_menu", "leo2_menu" };

		public override void OnInitializeMelon()
		{
			var cfg = MelonPreferences.CreateCategory("Leclerc SXXI");
			Enabled = cfg.CreateEntry("Enabled", true, description: "Adds the Leclerc SXXI as its own unit (Instant Action / mission editor, ATAC key \"LeclercSXXI\").");
			WeatheredPaint = PaintFinish.Create(cfg, FriendlyName);
			if (!Enabled.Value) return;
			var bundle = BundleAssets.Open(Path.Combine(MelonEnvironment.ModsDirectory, "LeclercSXXI", "leclercsxxi"), FriendlyName);
			Visual = BundleAssets.Prefab(bundle, "LeclercSXXI_visual", FriendlyName, required: true);
			Armor = BundleAssets.Prefab(bundle, "LeclercSXXI_armor", FriendlyName, required: false, ifMissing: "armour will not be changed.");
			if (Visual != null) MelonLogger.Msg($"Loaded {FriendlyName} visuals" + (Armor != null ? " and armour." : "."));
			var rig = bundle != null ? bundle.LoadAsset<TextAsset>(RigMap.AssetName) : null;
			if (rig != null) Rig = RigMap.Parse(rig.text);
			var roles = bundle != null ? bundle.LoadAsset<TextAsset>(MaterialRoles.AssetName) : null;
			if (roles != null) Roles = MaterialRoles.Parse(roles.text);
			SmokePoints = SmokeLaunchers.Load(bundle);
			SmokeDirections = SmokeLaunchers.LoadResource("smoke_directions.txt");
			Armour.Load(bundle);
			Ammo.Init();
			AirBurst.S = Ammo.ExplFuze;

			UnitRegistry.Add(new UnitDef
			{
				Key = UnitKey,
				FriendlyName = () => FriendlyName,
				BaseUnit = () => BaseUnit,
				ArmyFrom = () => ArmyFrom,
				Enabled = () => Enabled.Value,
				OnSpawn = Convert,
			});
			UnitRegistry.TryRegister();
			ShowcaseSpawner.Add(new ShowcaseSpawner.Entry { Key = UnitKey, Name = () => FriendlyName, Anchors = new[] { BaseUnit, "T80B", "T72M1" } });
		}

		public override void OnSceneWasInitialized(int buildIndex, string sceneName)
		{
			if (Enabled.Value) UnitRegistry.TryRegister();
		}

		public override void OnSceneWasLoaded(int buildIndex, string sceneName)
		{
			if (!Enabled.Value) return;
			ShowcaseSpawner.OnSceneLoaded(sceneName);
			if (MenuScenes.Contains(sceneName)) Sights.WarmDonors();
		}

		static void Convert(Vehicle vehicle)
		{
			if (Visual == null) return;
			Drive.Apply(vehicle);
			EngineAudio.Apply(vehicle);
			Armour.Apply(vehicle);
			BaseRigT80B.HideBaseVisuals(vehicle.transform, "LeclercSXXI_");
			BaseRigT80B.HideRunningGear(vehicle.transform, "LeclercSXXI_");
			VisualParts.Attach(vehicle.transform, Visual, "LeclercSXXI_", Bones);
			// after VisualParts.Attach: the blow-off lids are visual parts and the box looks them up by name
			Interior.Apply(vehicle);
			Ammo.Apply(vehicle);
			Gun.Apply(vehicle);
			PaintFinish.Apply(WeatheredPaint);
			var template = BaseRigT80B.FindTemplate(vehicle.transform);
			var flir = Shader.Find("Standard (FLIR)");
			foreach (var part in vehicle.transform.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("LeclercSXXI_")).ToArray())
				if (part.parent == null || !part.parent.name.StartsWith("LeclercSXXI_")) VehiclePaint.ApplyPaint(part.gameObject, template, flir, Painted);
			if (template == null) MelonLogger.Warning($"{FriendlyName}: no T-80B paint template found; the hull stays on the bundle's material.");
			VanillaMaterials.Apply(vehicle.transform, Roles);
			SmokeLaunchers.Grow(vehicle, SmokePoints?.Length ?? 0);
			SmokeLaunchers.Seat(vehicle, SmokePoints);
			SmokeLaunchers.OneVolley(vehicle);
			SmokeLaunchers.AimAlongTubes(vehicle, SmokeDirections);
			SmokeGrenadeDonor.Apply(vehicle, FriendlyName, SmokeFrom);
			SmokeCaps.Apply(vehicle.transform);
			MelonCoroutines.Start(SmokeLaunchers.RefreshCrewLater(vehicle, FriendlyName));
			// inferred: the mouth's centre from the model's cup, and the diesel plume's size (half the T-80B's).
			ExhaustMove.Place(vehicle.transform, Exhaust);
			HatchRig.Apply(vehicle.transform, Rig, "LeclercSXXI_", FriendlyName);
			var axes = PortAxes.FromRig(Rig, BaseRigT80B.TurretPivot, BaseRigT80B.GunPivot, FriendlyName);
			if (axes != null) PortAxes.Apply(vehicle, axes);
			else MelonLogger.Warning($"{FriendlyName}: bundle's rig map has no axes; the turret turns on the T-80B's pivots.");
			Gun.Recoil(vehicle);
			ConveyorLoader.Attach(vehicle);
			Sights.Apply(vehicle);
			AirBurst.Attach(vehicle);
			Coax.Apply(vehicle);
			Voices.Apply(vehicle);
			try { ThermalHeat.Apply(vehicle.transform, ThermalHeat.PortParts(vehicle.transform, "LeclercSXXI_")); }
			catch (System.Exception e) { MelonLogger.Error($"{FriendlyName} thermal heat failed: " + e); }
		}
	}
}
