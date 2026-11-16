using System;
using System.Linq;
using GHPC;
using GHPC.Vehicle;
using GHPC.Weaponry;
using GHPC.Weapons;
using LeclercSXXI.Common;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LeclercSXXI
{
	internal static partial class Coax
	{
		const string Tag = LeclercSXXIMod.FriendlyName;
		const string WeaponName = "12.7 mm M2HB";
		const string BeltName = "12.7 mm M2HB belt (Leclerc SXXI)";

		// The imported gun group's emtr_mg_flame_01 in prefab space, pointing forward.
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

		// The belt's first round: the HL 130's 12.7 drop scale (GunnerDayReticle).
		internal static AmmoCodexScriptable Round => _pattern?.FirstOrDefault();

		internal static void WarmDonor()
		{
			if (_pattern != null) return;
			var prefab = DonorParts.Prefab("M1IP");
			var donor = prefab?.GetComponent<WeaponsManager>()?.GetWeaponInfoByRole(WeaponSystemRole.RoofGun)?.Weapon;
			var pattern = donor?.Feed?.ReadyRack?.ClipTypes?.FirstOrDefault()?.MinimalPattern;
			if (pattern == null || pattern.Length == 0 || pattern.Any(round => round == null || round.AmmoType?.Caliber != 12.7f)) return;
			float cycle = RemoteMg.StagesSum(donor.Feed.RoundCycleStages);
			if (cycle <= 0f) return;

			// Copy the Browning's effects and retain its ammo assets and timing before the donor prefab can be unloaded.
			var flash = donor.MuzzleEffects?.FirstOrDefault(effect => effect != null);
			if (flash != null)
				_flash = DonorParts.Copy(prefab, "Leclerc coax muzzle flash", flash.gameObject).GetChild(0).gameObject;
			_cycle = cycle;
			_deviation = donor.BaseDeviationAngle;
			_loop = donor.WeaponSound?.LoopEventPath;
			_pattern = pattern;
		}

		// At spawn, after the gun setup: the M2HB on the T-80B's coax mount, its belts and the Browning's effects.
		internal static void Apply(Vehicle vehicle)
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

			var full = new AmmoType.AmmoClip { Name = BeltName, Capacity = Firepower.M2Belt, MinimalPattern = _pattern };
			var partial = new AmmoType.AmmoClip
			{
				Name = BeltName,
				Capacity = LeclercSXXIMod.CoaxRounds % Firepower.M2Belt,
				MinimalPattern = _pattern
			};
			rack.ClipTypes = new[] { full, partial };
			InitialClipCounts.SetValue(rack, new[] { LeclercSXXIMod.CoaxRounds / Firepower.M2Belt, 1 });
			rack.StoredClips.Clear();
			for (int belt = 0; belt < LeclercSXXIMod.CoaxRounds / Firepower.M2Belt; belt++) rack.StoredClips.Add(full);
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

		// GHPC matches belts by their cartridge pattern and fills the feed from the queued type's capacity.
		// Use the capacity of the actual belt that the rack is about to hand over, including the last 110 rounds.
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

		// The vanilla reserve counter assumes that every belt of a pattern has the same capacity.
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
