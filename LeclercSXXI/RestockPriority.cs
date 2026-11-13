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
				MelonLogger.Warning("Leclerc SXXI: could not give the main gun priority over restocking.");
				return;
			}

			var priority = loadout.GetComponent<RestockPriority>() ?? loadout.gameObject.AddComponent<RestockPriority>();
			priority._vehicle = vehicle;
			priority._feed = feed;
			loadout.RequireAzimuthLock = true;
			loadout.AzimuthLock = null;
		}

		[HarmonyPatch(typeof(LoadoutManager), "DoRestockTick")]
		static class RestockTick
		{
			static bool Prefix(LoadoutManager __instance)
			{
				try
				{
					var priority = __instance.GetComponent<RestockPriority>();
					if (priority == null || priority._feed == null || !priority._feed.Reloading) return true;
					return priority._vehicle.Neutralized || !CrewAvailable(__instance)
						|| __instance.CancelRestocking == LoadoutManager.RestockCancelMode.Cancel;
				}
				catch (Exception e) { PatchGuard.Failed("Leclerc restock pause", e); return true; }
			}
		}

		[HarmonyPatch(typeof(AmmoFeed), "CanContinueReloading", MethodType.Getter)]
		static class ReloadPermission
		{
			static void Postfix(AmmoFeed __instance, ref bool __result)
			{
				if (__result || __instance.ForcePauseReload) return;
				try
				{
					var priority = __instance.GetComponentInParent<RestockPriority>();
					if (priority != null && priority._feed == __instance) __result = true;
				}
				catch (Exception e) { PatchGuard.Failed("Leclerc reload priority", e); }
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
				catch (Exception e) { PatchGuard.Failed("Leclerc aiming during restock", e); }
			}
		}
	}
}
