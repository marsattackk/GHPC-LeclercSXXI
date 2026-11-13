using System;
using GHPC.Vehicle;
using LeclercSXXI.Common;
using MelonLoader;
using UnityEngine;

namespace LeclercSXXI
{
	internal static class Gun
	{
		static readonly GunSetup.Settings Settings = new GunSetup.Settings
		{
			Tag = LeclercSXXIMod.FriendlyName,
			TurretSpeed = 40f,
			GunSpeed = 30f,
			GunLimits = new Vector2(-8f, 20f),
			DepressionProfile = "gun_depression.txt",
			AutoloaderCycle = 6f,
			TypeChangeCycle = 8f,
			GunName = "120mm Gun CN120-26",
			CodexName = "LeclercSXXI CN120-26",
			CodexCaliberMm = 120f,
			TurretInertia = 0.95f,
			GunInertia = 0.95f,
			Deviation = 0.005f,
			ShotEvent = "event:/Weapons/canon_120mm-RH120",
		};

		static readonly GunSetup.Recoil RecoilSettings = new GunSetup.Recoil
		{
			Tag = LeclercSXXIMod.FriendlyName,
			Bone = "gun_recoil",
			Script = "Gun Animation",
			Barrel = "LeclercSXXI_GUN/GUN_BARREL",
			Travel = 0.40f,
		};

		internal static void Recoil(Vehicle vehicle)
		{
			try { GunSetup.BarrelRecoil(vehicle, RecoilSettings); }
			catch (Exception e) { MelonLogger.Error($"{LeclercSXXIMod.FriendlyName} barrel recoil failed: " + e); }
		}

		internal static void Apply(Vehicle vehicle)
		{
			try
			{
				ConveyorLoader.Configure(vehicle);
				GunSetup.Apply(vehicle, Settings);
				ReloadDip.Apply(vehicle);
				AutoloaderSound.Apply(vehicle);
			}
			catch (Exception e) { MelonLogger.Error($"{LeclercSXXIMod.FriendlyName} gun setup failed: " + e); }
		}
	}
}
