using System.Collections.Generic;
using System.Linq;
using System.Text;
using GHPC.Vehicle;
using LeclercSXXI.Common;
using MelonLoader;
using UnityEngine;

namespace LeclercSXXI
{
	internal static class Armour
	{
		static DmArmour _values;
		static VanillaEdits _edits;
		static readonly string[] StandaloneExterior = { "TurretRingNotSureAboutTheCrossection", "Commander_Cupola_Ring" };

		internal static void Load(AssetBundle bundle)
		{
			var values = bundle?.LoadAsset<TextAsset>(DmArmour.AssetName);
			var edits = bundle?.LoadAsset<TextAsset>(VanillaEdits.AssetName);
			_values = values == null ? null : DmArmour.Parse(values.text);
			_edits = edits == null ? null : VanillaEdits.Parse(edits.text);
			if (_edits != null) _edits.Off = _edits.Off.Except(StandaloneExterior).ToArray();
		}

		internal static void Apply(Vehicle vehicle)
		{
			if (_values == null || _edits == null || LeclercSXXIMod.Armor == null)
			{
				MelonLogger.Warning("Leclerc SXXI: armour is incomplete; keeping the donor armour.");
				return;
			}
			var problems = new List<string>();
			foreach (var col in vehicle.GetComponentsInChildren<Collider>(true).Where(c => StandaloneExterior.Contains(c.name)))
			{
				col.gameObject.SetActive(false);
			}
			StringBuilder table = null;
			var result = DmArmourApply.Install(vehicle, LeclercSXXIMod.Armor, _values, _edits, "Leclerc SXXI", problems, table);
			DmArmourApply.EmptyVolumes(vehicle, LeclercSXXIMod.Armor, problems);
			if (result.count == 0) problems.Add("none of the Leclerc's armour pieces fitted, so it has next to no armour");
			if (problems.Count > 0) MelonLogger.Warning("Leclerc SXXI armour: " + string.Join("; ", problems));
		}
	}
}
