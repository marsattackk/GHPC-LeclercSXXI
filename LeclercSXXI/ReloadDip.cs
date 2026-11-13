using System;
using System.Linq;
using GHPC;
using GHPC.Equipment.AzimuthLocking;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using LeclercSXXI.Common;
using MelonLoader;
using UnityEngine;

namespace LeclercSXXI
{
	internal sealed class ReloadDip : MonoBehaviour
	{
		internal const float LoadAngle = -1.8f;
		const float AlignmentTolerance = 0.05f;

		readonly AzimuthLock _lock = new AzimuthLock
		{
			PitchLockType = AzimuthLock.LockType.MoveToValue,
			DetachPlatform = true,
		};
		AimablePlatform _gun;
		TraverseLimits _clearance;
		Transform _turret, _muzzle;

		internal static void Apply(Vehicle vehicle)
		{
			var main = vehicle.GetComponent<WeaponsManager>()?.Weapons[0];
			var feed = main?.Weapon?.Feed;
			var gun = main?.FCS?.Mounts?.FirstOrDefault(p => p != null && p.Axis == EnumHelpers.Axis3.X);
			var turret = main?.FCS?.TurretPlatform?.Transform;
			if (feed == null || gun == null || turret == null || main.Weapon.MuzzleIdentity == null)
			{
				MelonLogger.Warning("Leclerc SXXI: main gun mount or feed missing, could not fit the reload dip.");
				return;
			}

			var dip = feed.GetComponent<ReloadDip>() ?? feed.gameObject.AddComponent<ReloadDip>();
			dip._gun = gun;
			dip._clearance = gun.GetComponent<TraverseLimits>();
			dip._turret = turret;
			dip._muzzle = main.Weapon.MuzzleIdentity;
			var inherited = feed.GetComponent<ReloadAzimuthLock>();
			if (inherited != null) inherited.enabled = false;
		}

		internal float Elevation => Mathf.Asin(Mathf.Clamp(Vector3.Dot(_muzzle.forward, _turret.up), -1f, 1f)) * Mathf.Rad2Deg;

		bool Align()
		{
			if (_gun == null || _gun.IsDetached || _turret == null || _muzzle == null) return false;
			if (_gun.CurrentLock != null && _gun.CurrentLock != _lock) return false;

			float min = _clearance != null ? _clearance.MinAt(_clearance.Yaw()) : _gun.LocalEulerLimits.x;
			_lock.PitchLockValue = Mathf.Max(LoadAngle, min);
			if (_gun.CurrentLock != _lock) _gun.SetLock(_lock);
			return min <= LoadAngle && Mathf.Abs(Elevation - LoadAngle) <= AlignmentTolerance;
		}

		void OnDisable() => Release();

		void Release()
		{
			if (_gun != null && _gun.CurrentLock == _lock) _gun.SetLock(null);
		}

		[HarmonyPatch(typeof(AmmoFeed), "Update")]
		static class ReloadClock
		{
			static void Prefix(AmmoFeed __instance, out (ReloadDip Dip, bool Paused) __state)
			{
				__state = default;
				if (!__instance.Reloading) return;
				var dip = __instance.GetComponent<ReloadDip>();
				if (dip == null || !dip.isActiveAndEnabled) return;
				try
				{
					bool pause = !dip.Align() && !__instance.ForcePauseReload;
					if (pause) __instance.ForcePauseReload = true;
					__state = (dip, pause);
				}
				catch (Exception e) { PatchGuard.Failed("Leclerc reload dip", e); }
			}

			static Exception Finalizer(AmmoFeed __instance, (ReloadDip Dip, bool Paused) __state, Exception __exception)
			{
				if (__state.Paused) __instance.ForcePauseReload = false;
				try
				{
					if (__state.Dip != null && !__instance.Reloading) __state.Dip.Release();
				}
				catch (Exception e) { PatchGuard.Failed("Leclerc reload dip", e); }
				return __exception;
			}
		}
	}
}
