using System.Collections.Generic;
using System.Linq;
using GHPC.Vehicle;
using LeclercSXXI.Common;
using UnityEngine;

namespace LeclercSXXI
{
	// The Leclerc's own armour pieces and compartments in place of the T-80B's. The pieces are the bundle's prefab, their
	// numbers Resources\dm_armour.txt (the bundle's copy only when the DLL has none).
	internal static partial class Armour
	{
		internal static GameObject Prefab;
		internal static DmArmour Values;
		// These exterior pieces live in the donor's separate AAR hierarchy, outside the armour collider groups.
		static readonly string[] StandaloneExterior = { "TurretRingNotSureAboutTheCrossection", "Commander_Cupola_Ring" };

		internal static void Load(AssetBundle bundle)
		{
			Prefab = BundleAssets.Prefab(
				bundle,
				"LeclercSXXI_armor",
				LeclercSXXIMod.FriendlyName,
				required: false,
				ifMissing: "armour will not be changed.");
			var values = PortText.Load(bundle, DmArmour.AssetName);
			var edits = PortText.Load(bundle, VanillaEdits.AssetName);
			Values = values == null ? null : DmArmour.Parse(values);
			LeclercSXXIMod.Edits = edits == null ? null : VanillaEdits.Parse(edits);
			if (LeclercSXXIMod.Edits != null) LeclercSXXIMod.Edits.Off = LeclercSXXIMod.Edits.Off.Except(StandaloneExterior).ToArray();
		}

		internal static void Apply(Vehicle vehicle)
		{
			if (Values == null || LeclercSXXIMod.Edits == null || Prefab == null)
			{
				DonorParts.WarnOnce(
					LeclercSXXIMod.FriendlyName,
					$"{LeclercSXXIMod.FriendlyName}: armour is incomplete; keeping the donor armour.");
				return;
			}
			var problems = new List<string>();
			foreach (var collider in vehicle
				.GetComponentsInChildren<Collider>(true)
				.Where(piece => StandaloneExterior.Contains(piece.name)))
			{
				collider.gameObject.SetActive(false);
			}
			var result = DmArmourApply.Install(vehicle, Prefab, Values, LeclercSXXIMod.Edits, LeclercSXXIMod.FriendlyName, problems);
			DmArmourApply.EmptyVolumes(vehicle, Prefab, problems);
			if (result.count == 0) problems.Add("none of the Leclerc's armour pieces fitted, so it has next to no armour");
			if (problems.Count > 0)
				DonorParts.WarnOnce(LeclercSXXIMod.FriendlyName, $"{LeclercSXXIMod.FriendlyName} armour: " + string.Join("; ", problems));
			DevApplied(vehicle, Values, null, problems);
		}

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		static partial void DevApplied(Vehicle vehicle, DmArmour values, string preset, List<string> problems);
	}
}
