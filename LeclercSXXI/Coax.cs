using System;
using System.Linq;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using HarmonyLib;
using LeclercSXXI.Common;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LeclercSXXI
{
	internal static class Coax
	{
		const string Tag = LeclercSXXIMod.FriendlyName;
		const string WeaponName = "12.7 mm M2HB";
		const string BeltName = "12.7 mm M2HB belt (Leclerc SXXI)";
		const int Load = 950, BeltSize = 140;

		static readonly Vector3 MuzzlePoint = new Vector3(-0.3470f, 1.7515f, 1.2932f);
		static readonly AccessTools.FieldRef<AmmoFeed, AmmoType.AmmoClip> LockedBelt =
			Reflect.FieldRefAccess<AmmoFeed, AmmoType.AmmoClip>("_queuedClipTypeLockedIn");
		static readonly AccessTools.FieldRef<AmmoFeed, float> CycleTime =
			Reflect.FieldRefAccess<AmmoFeed, float>("_totalCycleTime");
		static readonly System.Reflection.FieldInfo InitialClipCounts =
			Reflect.Field(typeof(GHPC.Weapons.AmmoRack), "_initialClipCounts");
		static readonly AccessTools.FieldRef<WeaponSystem, Transform> Muzzle =
			Reflect.FieldRefAccess<WeaponSystem, Transform>("_muzzleIdentity");
		static readonly AccessTools.FieldRef<WeaponSystem, ParticleSystem[]> MuzzleEffects =
			Reflect.FieldRefAccess<WeaponSystem, ParticleSystem[]>("_muzzleEffects");
		static readonly AccessTools.FieldRef<WeaponSystem, WeaponSystemCodexScriptable> Codex =
			Reflect.FieldRefAccess<WeaponSystem, WeaponSystemCodexScriptable>("CodexEntry");

		static AmmoCodexScriptable[] _pattern;
		static float _cycle, _deviation;
		static string _loop;
		static GameObject _flash;
		static WeaponSystemCodexScriptable _codex;

		internal static void WarmDonor()
		{
			if (_pattern != null) return;
			var prefab = DonorParts.Prefab("M1IP");
			var donor = prefab?.GetComponent<WeaponsManager>()?.GetWeaponInfoByRole(WeaponSystemRole.RoofGun)?.Weapon;
			var pattern = donor?.Feed?.ReadyRack?.ClipTypes?.FirstOrDefault()?.MinimalPattern;
			if (pattern == null || pattern.Length == 0 || pattern.Any(a => a == null || a.AmmoType?.Caliber != 12.7f)) return;
			float cycle = RemoteMg.StagesSum(donor.Feed.RoundCycleStages);
			if (cycle <= 0f) return;

			var flash = donor.MuzzleEffects?.FirstOrDefault(p => p != null);
			if (flash != null)
				_flash = DonorParts.Copy(prefab, "Leclerc coax muzzle flash", flash.gameObject).GetChild(0).gameObject;
			_cycle = cycle;
			_deviation = donor.BaseDeviationAngle;
			_loop = donor.WeaponSound?.LoopEventPath;
			_pattern = pattern;
		}

		internal static void Apply(Vehicle vehicle)
		{
			try { Fit(vehicle); }
			catch (Exception e) { MelonLogger.Error($"{Tag} M2HB coax setup failed: {e}"); }
		}

		static void Fit(Vehicle vehicle)
		{
			var info = vehicle.GetComponent<WeaponsManager>()?.GetWeaponInfoByRole(WeaponSystemRole.Coaxial);
			var weapon = info?.Weapon;
			var feed = weapon?.Feed;
			var rack = feed?.ReadyRack;
			if (IsCoax(feed)) return;
			var gun = RigBones.Gun(vehicle.transform);
			WarmDonor();
			if (rack == null || rack.UseVisibleRounds || gun == null || _pattern == null)
			{
				DonorParts.WarnOnce(Tag, $"{Tag}: could not fit the M2HB coax and its ammunition.");
				return;
			}

			foreach (var round in _pattern.Distinct())
				BallisticComputerRepository.Instance.RegisterAmmoType(round.AmmoType);

			var full = new AmmoType.AmmoClip { Name = BeltName, Capacity = BeltSize, MinimalPattern = _pattern };
			var partial = new AmmoType.AmmoClip { Name = BeltName, Capacity = Load % BeltSize, MinimalPattern = _pattern };
			rack.ClipTypes = new[] { full, partial };
			InitialClipCounts.SetValue(rack, new[] { Load / BeltSize, 1 });
			rack.StoredClips.Clear();
			for (int i = 0; i < Load / BeltSize; i++) rack.StoredClips.Add(full);
			rack.StoredClips.Add(partial);

			if (_codex == null)
			{
				_codex = ScriptableObject.CreateInstance<WeaponSystemCodexScriptable>();
				_codex.name = "leclerc_m2hb_coax";
				_codex.FriendlyName = WeaponName;
				_codex.CaliberMm = 12.7f;
				_codex.Type = WeaponSystemCodexScriptable.WeaponType.SmallArms;
			}
			info.Name = WeaponName;
			Codex(weapon) = _codex;
			weapon.BaseDeviationAngle = _deviation;
			RemoteMg.SetRate(weapon, 60f / _cycle);
			CycleTime(feed) = _cycle;

			var muzzle = new GameObject("M2HB coax muzzle").transform;
			muzzle.SetParent(gun, false);
			muzzle.SetPositionAndRotation(vehicle.transform.TransformPoint(MuzzlePoint), vehicle.transform.rotation);
			Muzzle(weapon) = muzzle;
			if (_flash != null)
			{
				var flash = Object.Instantiate(_flash, muzzle, false);
				flash.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
				MuzzleEffects(weapon) = new[] { flash.GetComponent<ParticleSystem>() };
			}
			if (weapon.WeaponSound != null && !string.IsNullOrEmpty(_loop))
			{
				weapon.WeaponSound.Transform = muzzle;
				weapon.WeaponSound.LoopEventPath = _loop;
			}
		}

		static bool IsCoax(AmmoFeed feed) => feed?.ReadyRack?.ClipTypes?.FirstOrDefault()?.Name == BeltName;

		[HarmonyPatch(typeof(AmmoFeed), "TryFinalFeedNewClip")]
		static class BeltLoadPatch
		{
			static void Prefix(AmmoFeed __instance)
			{
				if (!IsCoax(__instance)) return;
				ref var queued = ref LockedBelt(__instance);
				foreach (var belt in __instance.ReadyRack.StoredClips)
				{
					if (!belt.Equals(queued)) continue;
					queued = belt;
					break;
				}
			}
		}

		[HarmonyPatch(typeof(AmmoFeed), nameof(AmmoFeed.ReserveCount), MethodType.Getter)]
		static class ReservePatch
		{
			static bool Prefix(AmmoFeed __instance, ref int __result)
			{
				if (!IsCoax(__instance)) return true;
				__result = __instance.ReadyRack.GetCurrentStoredAmmoCount(__instance.ReadyRack.ClipTypes[0]);
				return false;
			}
		}
	}
}
