using System;
using System.Linq;
using GHPC;
using GHPC.Equipment.AzimuthLocking;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using LeclercSXXI.Common;
using UnityEngine;

namespace LeclercSXXI
{
	// The loading dip: while the autoloader loads, the CN120-26 goes to its loading angle (LoadAngle, against the turret)
	// and the reload's clock waits until the gun is there, while the HL 130 keeps the aim. Over the rear deck, where
	// the gun can't get down to LoadAngle, it loads at the lowest angle the deck allows instead of waiting for the turret.
	internal sealed partial class ReloadDip : MonoBehaviour
	{
		internal const float LoadAngle = -1.8f;   // elevation relative to the turret
		const float AlignmentTolerance = 0.05f;   // degrees from the loading angle that count as laid: my choice

		readonly AzimuthLock _lock = new AzimuthLock
		{
			PitchLockType = AzimuthLock.LockType.MoveToValue,
			DetachPlatform = true,   // give the optic the virtual aim while the physical gun moves
		};
		AimablePlatform _gun;
		TraverseLimits _clearance;
		Transform _turret, _muzzle;

		internal static void Apply(Vehicle vehicle)
		{
			var main = vehicle.GetComponent<WeaponsManager>()?.Weapons[0];
			var feed = main?.Weapon?.Feed;
			var gun = main?.FCS?.Mounts?.FirstOrDefault(mount => mount != null && mount.Axis == EnumHelpers.Axis3.X);
			var turret = main?.FCS?.TurretPlatform?.Transform;
			if (feed == null || gun == null || turret == null || main.Weapon.MuzzleIdentity == null)
			{
				DonorParts.WarnOnce(
					LeclercSXXIMod.FriendlyName,
					$"{LeclercSXXIMod.FriendlyName}: main gun mount or feed missing, could not fit the reload dip.");
				return;
			}

			var dip = feed.GetComponent<ReloadDip>() ?? feed.gameObject.AddComponent<ReloadDip>();
			dip.Init(gun, turret, main.Weapon.MuzzleIdentity);
			// Replace the T-80B's reload lock on this feed before it subscribes to a reload.
			var inherited = feed.GetComponent<ReloadAzimuthLock>();
			if (inherited != null) inherited.enabled = false;
			DevApplied(feed, dip, main.FCS, main.Weapon.MuzzleIdentity, turret);
		}

		void Init(AimablePlatform gun, Transform turret, Transform muzzle)
		{
			_gun = gun;
			_clearance = gun.GetComponent<TraverseLimits>();
			_turret = turret;
			_muzzle = muzzle;
		}

		internal float Elevation => Mathf.Asin(Mathf.Clamp(Vector3.Dot(_muzzle.forward, _turret.up), -1f, 1f)) * Mathf.Rad2Deg;

		bool Align()
		{
			if (_gun == null || _gun.IsDetached || _turret == null || _muzzle == null) return false;
			if (_gun.CurrentLock != null && _gun.CurrentLock != _lock) return false;

			float min = _clearance != null ? _clearance.MinAt(_clearance.Yaw()) : _gun.LocalEulerLimits.x;
			float angle = Mathf.Max(LoadAngle, min);
			_lock.PitchLockValue = angle;
			if (_gun.CurrentLock != _lock) _gun.SetLock(_lock);
			return Mathf.Abs(Elevation - angle) <= AlignmentTolerance;
		}

		void OnDisable() => Release();

		void Release()
		{
			if (_gun != null && _gun.CurrentLock == _lock) _gun.SetLock(null);
		}

		// Dev hooks: optional methods the author's own test harness fills in. They compile to nothing in a normal build.
		static partial void DevApplied(AmmoFeed feed, ReloadDip dip, FireControlSystem fcs, Transform muzzle, Transform turret);

		[HarmonyPatch(typeof(AmmoFeed), "Update")]
		static class ReloadClock
		{
			// Pause only while the feed ticks so the HUD can still distinguish reloading from restocking.
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
				catch (Exception exception) { PatchGuard.Failed("Leclerc reload dip", exception); }
			}

			static Exception Finalizer(AmmoFeed __instance, (ReloadDip Dip, bool Paused) __state, Exception __exception)
			{
				if (__state.Paused) __instance.ForcePauseReload = false;
				try
				{
					if (__state.Dip != null && !__instance.Reloading) __state.Dip.Release();
				}
				catch (Exception exception) { PatchGuard.Failed("Leclerc reload dip", exception); }
				return __exception;
			}
		}
	}
}
