using System;
using System.Collections.Generic;
using GHPC.Equipment;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weaponry;
using HarmonyLib;
using UnityEngine;

namespace LeclercSXXI.Common
{
	internal static class SmokeGrenadeDonor
	{
		internal sealed class Taken
		{
			internal GameObject Grenade;
			internal string From;
			internal float LaunchAngle;
			internal Vector2 Distance;
			internal object Calculation;
		}

		static readonly System.Reflection.FieldInfo PrefabField = Reflect.Field(typeof(VehicleSmokeManager), "_smokePrefab"),
			LaunchAngleField = Reflect.Field(typeof(VehicleSmokeManager), "_launchAngle"),
			DistanceField = Reflect.Field(typeof(VehicleSmokeManager), "_distanceRange"),
			CalculationField = Reflect.Field(typeof(VehicleSmokeManager), "_calculationType"),
			EffectField = Reflect.Field(typeof(Grenade), "_effectPrefab");

		static Transform _holder;
		static readonly Dictionary<string, Taken> Copies = new Dictionary<string, Taken>();
		static readonly HashSet<GameObject> Ours = new HashSet<GameObject>();

		internal static void Warm(string tag, string[] donors) => Take(tag, donors);

		static Taken Take(string tag, string[] donors)
		{
			string donorKey = string.Join(",", donors);
			if (Copies.TryGetValue(donorKey, out var had) && had.Grenade != null) return had;
			if (PrefabField == null || EffectField == null) return null;
			foreach (var key in donors)
			{
				var prefab = DonorParts.Prefab(key);
				var mgr = prefab != null ? prefab.GetComponentInChildren<VehicleSmokeManager>(true) : null;
				var grenadePrefab = mgr != null ? PrefabField.GetValue(mgr) as GameObject : null;
				var gren = grenadePrefab != null ? grenadePrefab.GetComponent<Grenade>() : null;
				if (gren == null) continue;
				if (_holder == null)
				{
					var holder = new GameObject("LeclercSXXI.Common donor smoke grenades (inactive copies)");
					holder.SetActive(false);
					UnityEngine.Object.DontDestroyOnLoad(holder);
					_holder = holder.transform;
				}
				var copy = UnityEngine.Object.Instantiate(grenadePrefab, _holder, false);
				copy.name = grenadePrefab.name;
				var effect = EffectField.GetValue(gren) as GameObject;
				if (effect != null)
				{
					var effectCopy = UnityEngine.Object.Instantiate(effect, _holder, false);
					effectCopy.name = effect.name;
					EffectField.SetValue(copy.GetComponent<Grenade>(), effectCopy);
				}
				var taken = new Taken
				{
					Grenade = copy,
					From = key,
					LaunchAngle = LaunchAngleField?.GetValue(mgr) is float angle ? angle : 45f,
					Distance = DistanceField?.GetValue(mgr) is Vector2 distance ? distance : new Vector2(200f, 350f),
					Calculation = CalculationField?.GetValue(mgr),
				};
				Copies[donorKey] = taken;
				Ours.Add(copy);
				return taken;
			}
			DonorParts.WarnOnce(
				"smoke grenade donor " + donorKey,
				$"{tag}: no smoke grenade donor ({string.Join(", ", donors)}) found; the base vehicle's grenade stays.");
			return null;
		}

		internal static string Apply(Vehicle v, string tag, string[] donors)
		{
			var mgr = v.GetComponentInChildren<VehicleSmokeManager>(true);
			if (mgr == null) return "no smoke manager";
			var taken = Take(tag, donors);
			if (taken == null) return "no donor grenade: the base vehicle's stays";
			var was = PrefabField.GetValue(mgr) as GameObject;
			PrefabField.SetValue(mgr, taken.Grenade);
			LaunchAngleField?.SetValue(mgr, taken.LaunchAngle);
			DistanceField?.SetValue(mgr, taken.Distance);
			if (taken.Calculation != null) CalculationField?.SetValue(mgr, taken.Calculation);
			ObjectPoolManager.Instance?.Register(taken.Grenade);
			return $"'{taken.Grenade.name}' from {taken.From} in place of '{was?.name ?? "none"}', {taken.LaunchAngle:0.#} deg, {taken.Distance.x:0}-{taken.Distance.y:0} m";
		}

		// The pool wipes its table during the after-action review. Re-register a copied grenade before every salvo.
		[HarmonyPatch(typeof(VehicleSmokeManager), nameof(VehicleSmokeManager.FireSmokes))]
		static class RegisterPatch
		{
			static void Prefix(VehicleSmokeManager __instance)
			{
				try
				{
					if (Ours.Count == 0 || PrefabField == null) return;
					if (PrefabField.GetValue(__instance) is GameObject grenade && Ours.Contains(grenade))
						ObjectPoolManager.Instance?.Register(grenade);
				}
				catch (Exception exception) { PatchGuard.Failed("Donor smoke grenade pool", exception); }
			}
		}
	}
}
