using GHPC.Vehicle;
using LeclercSXXI.Common;
using MelonLoader;
using NWH.VehiclePhysics;

namespace LeclercSXXI
{
	internal static class Drive
	{
		// Ratios are inferred from the ESM 500's 5.36 spread. On this rig, 0.42 m wheels
		// at 2500 rpm in direct drive need a 5.5125 final drive for 72 km/h. NWH accounts for track-wheel enlargement.
		internal static readonly Mobility.Settings Settings = new Mobility.Settings
		{
			Tag = LeclercSXXIMod.FriendlyName,
			MaxPower = 1500f,
			MaxRpm = 2500f,
			MinRpm = 900f,
			// The T-80B turbine's 1000 rpm/s held each low gear flat for ~1 s and 0-32 km/h took 6-7 s against the maker's
			// < 6 s. The Leopard 2A3 diesel's 1500 rpm/s (GHPC's own; also the engine-sound donor).
			MaxRpmChange = 1500f,
			FinalDrive = 5.5125f,
			// 0.95 x MaxRpm: NWH clamps the up-shift there, so a higher value would never shift up.
			ShiftUpRpm = 2375f,
			ForwardGears = new[] { 5.36f, 3.52f, 2.32f, 1.52f, 1f },
			// Fast reverse, 40 km/h as the vanilla Leopard 2A3 / M1IP (sourced 38): -1.895 x 38 / 40.
			ReverseGears = new[] { -5.36f, -1.80f },
			// Leave the cruise controller room above the gearing limits to apply full throttle.
			MaxForwardKmh = 82f,
			ReverseCapKmh = 48f,   // my choice: 20 % over the fast reverse gear's 40 km/h
			CombatMassKg = 57700f,
			LinearDrag = 0.072f,   // GHPC resistance calibration
		};

		const float BrakeTorque = 10900f;

		internal static void Apply(Vehicle vehicle)
		{
			Mobility.Apply(vehicle, Settings);
			var controller = vehicle.GetComponent<VehicleController>();
			if (controller == null)
			{
				MelonLogger.Warning($"{Settings.Tag}: vehicle controller missing.");
				return;
			}
			// NWH reverses the source list in place each time it rebuilds its combined gear table.
			// The setter already built the correct table. Restore the input order for the rebuild in Initialize.
			if (!controller.IsInitialized) controller.transmission.ReverseGears.Reverse();
			// GHPC also applies this intensity to reverse throttle. The T-80B's 0.30 held about 31 km/h in high reverse.
			controller.drivingAssists.cruiseControl.overspeedBrakeIntensity = 0.5f;
			// Brakes a little better than the game's own Western tanks. Torque x wheels / radius / mass:
			// Leopard 2A3 7500 x 14 / 0.40 / 55.3 t = 4.7 m/s², M1IP 7500 x 14 / 0.37 = 5.1, T-80B 10000 x 12 / 0.42 / 46.5 t = 6.1.
			// 10900 x 12 / 0.42 / 57.7 t = 5.4, about 5 % over the Abrams. Full brakes also stop a reverse run before the
			// gearbox takes forward (NWH waits for under 1 m/s), so this sets how quickly the tank changes direction.
			controller.brakes.maxTorque = BrakeTorque;
		}
	}
}
