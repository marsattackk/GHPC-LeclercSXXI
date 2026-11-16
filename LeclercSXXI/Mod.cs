using System.IO;
using LeclercSXXI.Common;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(LeclercSXXI.LeclercSXXIMod), "Leclerc SXXI", "0.2.1", "marsattackk")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace LeclercSXXI
{
	// Loads the bundle, registers the unit on the T80B prefab and hangs the War Thunder model's HULL / TURRET / GUN on the base
	// rig's bones (the base's visuals off, paint, real axes). Imported armour and four compartments are applied before the axes
	// move; crew and ammunition, weapons and sights are fitted in their own stages (Conversion.Convert).
	public partial class LeclercSXXIMod : MelonMod
	{
		internal const string UnitKey = "LeclercSXXI";

		internal static GameObject Visual;
		internal static VanillaEdits Edits;   // the T-80B's pieces switched off or changed (Resources\vanilla_edits.txt)
		internal static MaterialRoles Roles;   // material roles: the vanilla glass at spawn
		internal static RigMap Rig;   // moving parts: hatches, running gear
		internal static Vector3[] SmokePoints;   // the smoke tubes' launch points (from the bundle)
		internal static Vector3[] SmokeDirections;   // each tube's firing direction, same order (Resources\smoke_directions.txt)

		internal const string FriendlyName = "Leclerc SXXI";
		const string BaseUnit = "T80B";   // registry key of the vanilla prefab "T80B" (crew, interior, damage model, running gear)
		const string ArmyFrom = "M60A1";   // the NATO side, US army: the game has no French army
		internal const int CoaxRounds = 950;   // the 12.7 mm coax's load, the maker's figure (others say 800 or 1100)

		internal static MelonPreferences_Entry<bool> Enabled;
		internal static CampaignConfig Campaign;
		internal static MelonPreferences_Entry<bool> WeatheredPaint;

		const string Repo = "marsattackk/GHPC-LeclercSXXI";
		// A changed default needs a rule here. MelonLoader keeps the saved one. None yet.
		static readonly SettingsMigration[] Migrations = { };

		public override void OnInitializeMelon()
		{
			BuildStamp.Log(Info.Name);
			UnityErrors.Install(typeof(LeclercSXXIMod).Namespace);
			CreateSettings();
			if (!Enabled.Value) return;
			PortAmmo.S = Firepower.Settings();   // the rounds' settings (the mission customizer asks for them at the menu)
			AirBurst.S = Firepower.Fuse;
			LoadData();
			RegisterUnit();
		}

		// The settings in MelonPreferences.cfg, then the file's version and its migrations.
		void CreateSettings()
		{
			var cfg = MelonPreferences.CreateCategory("Leclerc SXXI");
			Enabled = cfg.CreateEntry("Enabled", true, description: SettingText.Enabled);
			// Rare (0.25), after two missions, on the side that fields the M60A1 (the American one).
			Campaign = CampaignConfig.Create(cfg, FriendlyName, 0.25f, 2, "American");
			WeatheredPaint = PaintFinish.Create(cfg, FriendlyName);   // "Weathered paint" (PaintFinish, as the T-90s / T-80UM2)
			UpdateCheck.Config(cfg, FriendlyName, Info.Version, Repo);
			DevInit(cfg);
			PortSettings.Finish(cfg, Info.Version, Migrations);
		}

		// The bundle's prefabs and the text data in Resources\ (armour, base edits, rig, glass, smoke points).
		static void LoadData()
		{
			var bundle = BundleAssets.Open(Path.Combine(MelonEnvironment.ModsDirectory, "LeclercSXXI", "leclercsxxi"), FriendlyName);
			Visual = BundleAssets.Prefab(bundle, "LeclercSXXI_visual", FriendlyName, required: true);
			Armour.Load(bundle);
			if (Visual != null) MelonLogger.Msg($"Loaded {FriendlyName} visuals" + (Armour.Prefab != null ? " and armour." : "."));
			var rig = PortText.Load(bundle, RigMap.AssetName);
			if (rig != null) Rig = RigMap.Parse(rig);
			else MelonLogger.Warning($"{FriendlyName}: no rig map; the hatches stay closed.");
			var roles = PortText.Load(bundle, MaterialRoles.AssetName);
			if (roles != null) Roles = MaterialRoles.Parse(roles);
			else MelonLogger.Warning($"{FriendlyName}: no material roles; glass keeps the bundle's own materials.");
			SmokePoints = SmokeLaunchers.Load(bundle);
			if (bundle != null && SmokePoints == null)
				MelonLogger.Warning($"{FriendlyName}: no smoke launch points; the smoke grenades leave from the T-80B's positions.");
			SmokeDirections = SmokeLaunchers.LoadResource("smoke_directions.txt");
			DevLoadData(bundle);
		}

		// The Leclerc SXXI as a unit of its own: the game's unit lists and the Showcase.
		static void RegisterUnit()
		{
			var unit = new UnitDef
			{
				Key = UnitKey,
				FriendlyName = () => FriendlyName,
				BaseUnit = () => BaseUnit,
				ArmyFrom = () => ArmyFrom,
				Enabled = () => Enabled.Value,
				OnSpawn = vehicle => Conversion.Convert(vehicle, atSpawn: true),
			};
			Campaign.Use(unit);
			UnitRegistry.Add(unit);
			UnitRegistry.TryRegister();
			ShowcaseSpawner.Add(new ShowcaseSpawner.Entry
			{
				Key = UnitKey,
				Name = () => FriendlyName,
				Anchors = new[] { BaseUnit, "T72M1" }
			});
		}

		public override void OnSceneWasInitialized(int buildIndex, string sceneName)
		{
			if (Enabled.Value) UnitRegistry.TryRegister();
		}

		public override void OnSceneWasLoaded(int buildIndex, string sceneName)
		{
			if (!Enabled.Value) return;
			UpdateCheck.OnSceneLoaded(MenuScenes.Contains(sceneName));
			UnitRegistry.StartCampaignRoster();
			ShowcaseSpawner.OnSceneLoaded(sceneName);
			if (MenuScenes.Contains(sceneName)) Sights.WarmDonors();
			DevSceneLoaded(sceneName, MenuScenes.Contains(sceneName), HarmonyInstance);
			if (!MenuScenes.Contains(sceneName))
				ConvertSweep.AtPlayerReady(UnitKey, vehicle => Conversion.Convert(vehicle));
		}

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		static partial void DevInit(MelonPreferences_Category cfg);
		static partial void DevLoadData(AssetBundle bundle);
		static partial void DevSceneLoaded(string sceneName, bool menu, HarmonyLib.Harmony harmony);
	}
}
