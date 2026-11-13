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
			string k = string.Join(",", donors);
			if (Copies.TryGetValue(k, out var had) && had.Grenade != null) return had;
			if (PrefabField == null || EffectField == null) return null;
			foreach (var key in donors)
			{
				var prefab = DonorParts.Prefab(key);
				var mgr = prefab != null ? prefab.GetComponentInChildren<VehicleSmokeManager>(true) : null;
				var gp = mgr != null ? PrefabField.GetValue(mgr) as GameObject : null;
				var gren = gp != null ? gp.GetComponent<Grenade>() : null;
				if (gren == null) continue;
				if (_holder == null) { var h = new GameObject("LeclercSXXI.Common donor smoke grenades (inactive copies)"); h.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(h); _holder = h.transform; }
				var g = UnityEngine.Object.Instantiate(gp, _holder, false);
				g.name = gp.name;
				var fx = EffectField.GetValue(gren) as GameObject;
				if (fx != null)
				{
					var fc = UnityEngine.Object.Instantiate(fx, _holder, false);
					fc.name = fx.name;
					EffectField.SetValue(g.GetComponent<Grenade>(), fc);
				}
				var t = new Taken
				{
					Grenade = g, 					LaunchAngle = LaunchAngleField?.GetValue(mgr) is float a ? a : 45f,
					Distance = DistanceField?.GetValue(mgr) is Vector2 d ? d : new Vector2(200f, 350f),
					Calculation = CalculationField?.GetValue(mgr),
				};
				Copies[k] = t;
				Ours.Add(g);
				return t;
			}
			DonorParts.WarnOnce("smoke grenade donor " + k, $"{tag}: no smoke grenade donor ({string.Join(", ", donors)}) found; the base vehicle's grenade stays.");
			return null;
		}

		internal static void Apply(Vehicle v, string tag, string[] donors)
		{
			var mgr = v.GetComponentInChildren<VehicleSmokeManager>(true);
			if (mgr == null) return;
			var t = Take(tag, donors);
			if (t == null) return;
			PrefabField.SetValue(mgr, t.Grenade);
			LaunchAngleField?.SetValue(mgr, t.LaunchAngle);
			DistanceField?.SetValue(mgr, t.Distance);
			if (t.Calculation != null) CalculationField?.SetValue(mgr, t.Calculation);
			ObjectPoolManager.Instance?.Register(t.Grenade);
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
					if (PrefabField.GetValue(__instance) is GameObject g && Ours.Contains(g)) ObjectPoolManager.Instance?.Register(g);
				}
				catch (Exception e) { PatchGuard.Failed("Donor smoke grenade pool", e); }
			}
		}
	}
}
