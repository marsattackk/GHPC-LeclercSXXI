using GHPC.Vehicle;
using LeclercSXXI.Common;
using MelonLoader;
using NWH.VehiclePhysics;

namespace LeclercSXXI
{
	internal static class Drive
	{
		internal static readonly Mobility.Settings Settings = new Mobility.Settings
		{
			Tag = LeclercSXXIMod.FriendlyName,
			MaxPower = 1500f,
			MaxRpm = 2500f,
			MinRpm = 900f,
			MaxRpmChange = 1500f,
			FinalDrive = 5.5125f,
			ShiftUpRpm = 2375f,
			ForwardGears = new[] { 5.36f, 3.52f, 2.32f, 1.52f, 1f },
			ReverseGears = new[] { -5.36f, -1.80f },
			MaxForwardKmh = 82f,
			ReverseCapKmh = 48f,
			CombatMassKg = 57700f,
			LinearDrag = 0.072f,
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
			if (!controller.IsInitialized) controller.transmission.ReverseGears.Reverse();
			controller.drivingAssists.cruiseControl.overspeedBrakeIntensity = 0.5f;
			controller.brakes.maxTorque = BrakeTorque;
		}
	}
}
