using GHPC.Equipment;
using GHPC.Vehicle;
using LeclercSXXI.Common;
using UnityEngine;

namespace LeclercSXXI
{
	// The Leclerc's 14 GALIX smoke tubes: the tubes' points and directions (SmokeLaunchers, from the bundle's launch points and
	// Resources\smoke_directions.txt), the grenade they throw and the caps over them.
	internal static class Countermeasures
	{
		// The smoke grenade and its throw: the Leopard 2's M82 66 mm, first registry key found.
		internal static readonly string[] SmokeFrom = { "LEO2A3", "LEO2A4", "LEO2" };

		// The base vehicle's grenade slots onto the port's tubes (SmokeLaunchers), then each tube's cap (built into the
		// bundle) given to its slot, hidden when that tube fires; the launcher's crew cache refreshed once the crew exists
		// (RefreshCrew, a late step: a spawned unit's launcher otherwise never passes GHPC's crew check).
		// 14 GALIX tubes on the T-80B's 8 slots: slots added, all fired in one volley, each grenade along its tube
		// (Resources\smoke_directions.txt; the rear corner pairs fire back and outward, inferred).
		internal static void SeatLaunchers(Vehicle vehicle)
		{
			SmokeLaunchers.Grow(vehicle, LeclercSXXIMod.SmokePoints?.Length ?? 0);
			SmokeLaunchers.Seat(vehicle, LeclercSXXIMod.SmokePoints);
			SmokeLaunchers.OneVolley(vehicle);
			SmokeLaunchers.AimAlongTubes(vehicle, LeclercSXXIMod.SmokeDirections);
		}

		// The Leopard 2's M82 grenade and throw in place of the T-80B's 3Д6 (SmokeGrenadeDonor).
		internal static void FitGrenade(Vehicle vehicle)
		{
			SmokeGrenadeDonor.Apply(vehicle, LeclercSXXIMod.FriendlyName, SmokeFrom);
		}

		internal static void FitCaps(Transform root)
		{
			SmokeCaps.Apply(root);
		}

		// Two frames after the spawn (Conversion.LateSteps), when the crew exists.
		internal static void RefreshCrew(Vehicle vehicle)
		{
			var smoke = vehicle.GetComponentInChildren<VehicleSmokeManager>(true);
			if (smoke != null) SmokeLaunchers.RefreshCrew(smoke);
		}
	}
}
