using System;
using System.Reflection;
using GHPC.Crew;
using GHPC.Vehicle;
using GHPC.Weapons;
using HarmonyLib;
using LeclercSXXI.Common;
using MelonLoader;
using UnityEngine;

namespace LeclercSXXI
{
	// The hull-to-bustle transfer yields to the gun's reload, then continues its current segment.
	internal sealed class RestockPriority : MonoBehaviour
	{
		static readonly Func<LoadoutManager, bool> CrewAvailable =
			Reflect.Property(typeof(LoadoutManager), "_crewAvailableForRestock")?.GetGetMethod(true) is MethodInfo getter
				? AccessTools.MethodDelegate<Func<LoadoutManager, bool>>(getter) : null;

		Vehicle _vehicle;
		AmmoFeed _feed;

		internal static void Apply(Vehicle vehicle)
		{
			var loadout = vehicle.GetComponent<LoadoutManager>();
			var feed = vehicle.GetComponent<WeaponsManager>()?.Weapons[0]?.Weapon?.Feed;
			if (loadout == null || feed == null || CrewAvailable == null)
			{
				MelonLogger.Warning($"{LeclercSXXIMod.FriendlyName}: could not give the main gun priority over restocking.");
				return;
			}

			var priority = loadout.GetComponent<RestockPriority>() ?? loadout.gameObject.AddComponent<RestockPriority>();
			priority.Init(vehicle, feed);
			// StartRestock switches the stabilizer off in its non-locking branch. The lock branch with no lock
			// leaves the fire control alone and imposes no angle requirement on the transfer.
			loadout.RequireAzimuthLock = true;
			loadout.AzimuthLock = null;
		}

		void Init(Vehicle vehicle, AmmoFeed feed)
		{
			_vehicle = vehicle;
			_feed = feed;
		}

		// What the patches below ask: whether this tank's main gun is reloading, whether the tank is out, whose feed it is.
		bool GunReloading => _feed != null && _feed.Reloading;

		bool Neutralized => _vehicle.Neutralized;

		bool Feeds(AmmoFeed feed) => _feed == feed;

		[HarmonyPatch(typeof(LoadoutManager), "DoRestockTick")]
		static class RestockTick
		{
			static bool Prefix(LoadoutManager __instance)
			{
				try
				{
					var priority = __instance.GetComponent<RestockPriority>();
					if (priority == null || !priority.GunReloading) return true;
					// Let the stock tick process crew loss and a cancellation that returns the round to the drum.
					return priority.Neutralized || !CrewAvailable(__instance)
						|| __instance.CancelRestocking == LoadoutManager.RestockCancelMode.Cancel;
				}
				catch (Exception exception)
				{
					PatchGuard.Failed("Leclerc restock pause", exception);
					return true;
				}
			}
		}

		[HarmonyPatch(typeof(AmmoFeed), "CanContinueReloading", MethodType.Getter)]
		static class ReloadPermission
		{
			static void Postfix(AmmoFeed __instance, ref bool __result)
			{
				// ReloadDip uses ForcePauseReload while the gun is travelling to the loading angle.
				if (__result || __instance.ForcePauseReload) return;
				try
				{
					var priority = __instance.GetComponentInParent<RestockPriority>();
					if (priority != null && priority.Feeds(__instance)) __result = true;
				}
				catch (Exception exception) { PatchGuard.Failed("Leclerc reload priority", exception); }
			}
		}

		[HarmonyPatch(typeof(CrewBrainWeaponsModule), nameof(CrewBrainWeaponsModule.BusyRestocking), MethodType.Getter)]
		static class CrewMovement
		{
			static void Postfix(CrewBrainWeaponsModule __instance, ref bool __result)
			{
				if (!__result) return;
				try
				{
					if (__instance.LoadoutManager.GetComponent<RestockPriority>() != null) __result = false;
				}
				catch (Exception exception) { PatchGuard.Failed("Leclerc aiming during restock", exception); }
			}
		}
	}
}
